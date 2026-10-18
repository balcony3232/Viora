using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VioraManager;

/// <summary>
/// Maps a running Roblox Player process to its own local Player log using
/// process and log creation timestamps. Only reads recent log tails to extract
/// public game/place IDs. Never reads, stores or sends Roblox account tokens.
/// Log matching is best-effort and deliberately never shares one log between PIDs.
/// </summary>
internal sealed class RobloxInstanceGames
{
    internal readonly record struct RunningProcess(int Pid, DateTime StartedUtc);

    private sealed class Session
    {
        public required DateTime StartedUtc { get; init; }
        public string? LogPath { get; set; }
        public DateTime LastLogWriteUtc { get; set; }
        public long? PlaceId { get; set; }
        public long? UniverseId { get; set; }
    }

    private sealed record GameCacheEntry(string? Name, DateTime ExpiresUtc);

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(6) };
    private static readonly Regex PlaceJoin = new(
        @"(?:!\s*Joining game\s+'[^']*'\s+place\s+|Report game_join_loadtime:\s*placeid:)\s*(\d{1,19})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UniverseJoin = new(
        @"(?:Report game_join_loadtime:|game_join_loadtime)[^\r\n]*?\buniverseid:\s*(\d{1,19})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Dictionary<int, Session> _sessions = new();
    private readonly Dictionary<long, GameCacheEntry> _nameCache = new();
    private readonly HashSet<long> _inFlight = new();
    private readonly Action _onNamesChanged;
    private DateTime _lastLogScanUtc = DateTime.MinValue;

    public RobloxInstanceGames(Action onNamesChanged) => _onNamesChanged = onNamesChanged;

    public void Observe(IReadOnlyList<RunningProcess> processes)
    {
        var current = processes.ToDictionary(p => p.Pid);
        foreach (var oldPid in _sessions.Keys.Where(pid =>
            !current.TryGetValue(pid, out var p) || p.StartedUtc != _sessions[pid].StartedUtc).ToArray())
            _sessions.Remove(oldPid);

        foreach (var process in processes)
            if (!_sessions.ContainsKey(process.Pid))
                _sessions[process.Pid] = new Session { StartedUtc = process.StartedUtc };

        // A new Roblox process can take a few seconds to create its log.
        // Rescan at most every 6 seconds; existing associations remain stable.
        if (DateTime.UtcNow - _lastLogScanUtc >= TimeSpan.FromSeconds(6) &&
            _sessions.Values.Any(s => s.LogPath is null))
        {
            AssignLogs();
            _lastLogScanUtc = DateTime.UtcNow;
        }

        foreach (var session in _sessions.Values)
        {
            if (session.LogPath is not null)
                ReadLatestGameJoin(session);
            if (session.PlaceId is long placeId && placeId > 0) QueueLookup(placeId, isUniverse: false);
            else if (session.UniverseId is long universeId && universeId > 0) QueueLookup(universeId, isUniverse: true);
        }
    }

    private void AssignLogs()
    {
        string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");
        if (!Directory.Exists(logDirectory)) return;
        try
        {
            // Some Roblox versions change log naming conventions; filter Studio
            // and require a Player marker when present rather than reading all logs.
            var files = Directory.EnumerateFiles(logDirectory, "*.log")
                .Where(path => !Path.GetFileName(path).Contains("Studio", StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path))
                .Where(info => info.Exists &&
                    (info.Name.Contains("Player", StringComparison.OrdinalIgnoreCase) ||
                     info.Name.Contains("Roblox", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(info => info.CreationTimeUtc)
                .Take(100).ToArray();

            var claimed = _sessions.Values
                .Where(s => s.LogPath is not null)
                .Select(s => s.LogPath!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Each session gets the unclaimed log closest to its start. A 120s
            // tolerance accounts for slow startup but excludes stale sessions.
            var options = (
                from s in _sessions.Values where s.LogPath is null
                from file in files
                let seconds = Math.Abs((file.CreationTimeUtc - s.StartedUtc).TotalSeconds)
                where seconds <= 120
                orderby seconds
                select (s, file, seconds)).ToArray();

            var assigned = new HashSet<Session>();
            foreach (var (session, file, _) in options)
            {
                if (assigned.Contains(session) || !claimed.Add(file.FullName)) continue;
                session.LogPath = file.FullName;
                assigned.Add(session);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Log access is optional; window and memory monitoring still work.
        }
    }

    private static void ReadLatestGameJoin(Session session)
    {
        try
        {
            var file = new FileInfo(session.LogPath!);
            if (!file.Exists || file.Length == 0 || file.LastWriteTimeUtc == session.LastLogWriteUtc)
                return;
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(-Math.Min(stream.Length, 256 * 1024), SeekOrigin.End);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string tail = reader.ReadToEnd();
            session.LastLogWriteUtc = file.LastWriteTimeUtc;

            var places = PlaceJoin.Matches(tail);
            var universes = UniverseJoin.Matches(tail);
            // Read the latest place (preferred), not a stale join from the start
            // of the session after a Roblox teleport or experience change.
            if (places.Count > 0 && long.TryParse(places[^1].Groups[1].Value, out long place) && place > 0)
            {
                session.PlaceId = place;
                session.UniverseId = null;
            }
            else if (universes.Count > 0 &&
                long.TryParse(universes[^1].Groups[1].Value, out long universe) && universe > 0)
            {
                session.PlaceId = null;
                session.UniverseId = universe;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Roblox can rotate or lock logs; never fail the Manager.
        }
    }

    public string GetGameName(int pid)
    {
        if (!_sessions.TryGetValue(pid, out var session)) return "Game not detected";
        long? id = session.PlaceId ?? session.UniverseId;
        if (id is not long validId) return session.LogPath is null ? "Detecting game…" : "Game not detected";
        long key = CacheKey(validId, session.PlaceId is null);
        if (_nameCache.TryGetValue(key, out var entry))
            return entry.Name ?? $"Game ID {validId} (name unavailable)";
        return "Finding game name…";
    }

    private static long CacheKey(long id, bool isUniverse) => isUniverse ? -id : id;

    private void QueueLookup(long id, bool isUniverse)
    {
        long key = CacheKey(id, isUniverse);
        if (_inFlight.Contains(key)) return;
        if (_nameCache.TryGetValue(key, out var cached) && DateTime.UtcNow < cached.ExpiresUtc) return;

        _inFlight.Add(key);
        _ = ResolveNameAsync(id, isUniverse, key);
    }

    private async Task ResolveNameAsync(long id, bool isUniverse, long key)
    {
        string? name = null;
        try
        {
            long universeId = id;
            if (!isUniverse)
            {
                using var universeResponse = await Client.GetAsync(
                    $"https://apis.roblox.com/universes/v1/places/{id}/universe");
                if (!universeResponse.IsSuccessStatusCode) return;
                using var universeJson = JsonDocument.Parse(await universeResponse.Content.ReadAsStringAsync());
                if (!universeJson.RootElement.TryGetProperty("universeId", out var number) ||
                    !number.TryGetInt64(out universeId) || universeId <= 0) return;
            }

            using var gameResponse = await Client.GetAsync(
                $"https://games.roblox.com/v1/games?universeIds={universeId}");
            if (!gameResponse.IsSuccessStatusCode) return;
            using var gameJson = JsonDocument.Parse(await gameResponse.Content.ReadAsStringAsync());
            if (gameJson.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0 &&
                data[0].TryGetProperty("name", out var title) && title.ValueKind == JsonValueKind.String)
                name = title.GetString()?.Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            // Network lookup is optional and never contains player identity data.
        }
        finally
        {
            _nameCache[key] = new GameCacheEntry(
                string.IsNullOrWhiteSpace(name) ? null : name,
                DateTime.UtcNow.AddMinutes(name is null ? 3 : 30));
            _inFlight.Remove(key);
            _onNamesChanged();
        }
    }
}
