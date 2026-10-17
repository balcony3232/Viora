using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Viora;

/// <summary>Read-only, best-effort detection of a game's place ID in this PC's Roblox Player logs.</summary>
public static class RobloxGameDetection
{
    private static readonly Regex GameJoin = new(
        @"(?:!\s*Joining game\s+'[^']+'\s+place\s+|Report game_join_loadtime:\s*placeid:)\s*(\d{1,19})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static long? FindLatestPlaceId(DateTimeOffset firstSeen)
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");
            if (!Directory.Exists(folder)) return null;
            // Don't use old logs when Roblox is merely sitting at its home screen.
            // If Viora was opened halfway through a long session, detection may be unavailable.
            var files = Directory.EnumerateFiles(folder, "*.log")
                .Concat(Directory.EnumerateFiles(folder, "*.txt"))
                .Select(name => new FileInfo(name))
                .Where(file => file.Exists && file.Length > 0 && file.LastWriteTimeUtc >= firstSeen.UtcDateTime.AddSeconds(-20))
                .OrderByDescending(file => file.LastWriteTimeUtc).Take(4);
            foreach (var file in files)
            {
                // Only Roblox Player logs; exclude Studio logs when their filenames are identifiable.
                if (file.Name.Contains("Studio", StringComparison.OrdinalIgnoreCase)) continue;
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(-Math.Min(stream.Length, 512 * 1024), SeekOrigin.End);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var recent = reader.ReadToEnd();
                var matches = GameJoin.Matches(recent);
                if (matches.Count > 0 && long.TryParse(matches[matches.Count - 1].Groups[1].Value, out var placeId) && placeId > 0)
                    return placeId;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { /* Roblox can rotate or lock its own logs at any time. */ }
        return null;
    }
}

/// <summary>Optional PUBLIC Roblox API lookup; never sends player identities or full log lines.</summary>
public sealed class RobloxGameNames
{
    private readonly Dictionary<long, (string? Name, DateTimeOffset Checked)> _cache = new();
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<string?> GetNameAsync(long placeId)
    {
        if (_cache.TryGetValue(placeId, out var cached) && DateTimeOffset.UtcNow - cached.Checked < TimeSpan.FromMinutes(30))
            return cached.Name;
        string? result = null;
        try
        {
            using var universeResponse = await Client.GetAsync($"https://apis.roblox.com/universes/v1/places/{placeId}/universe");
            if (universeResponse.IsSuccessStatusCode)
            {
                using var universe = JsonDocument.Parse(await universeResponse.Content.ReadAsStringAsync());
                if (universe.RootElement.TryGetProperty("universeId", out var idElement) &&
                    idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt64(out long universeId) && universeId > 0)
                {
                    using var gameResponse = await Client.GetAsync($"https://games.roblox.com/v1/games?universeIds={universeId}");
                    if (gameResponse.IsSuccessStatusCode)
                    {
                        using var games = JsonDocument.Parse(await gameResponse.Content.ReadAsStringAsync());
                        if (games.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array &&
                            data.GetArrayLength() > 0 && data[0].TryGetProperty("name", out var nameElement))
                        {
                            result = nameElement.GetString()?.Trim();
                            if (string.IsNullOrWhiteSpace(result)) result = null;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        { /* Unknown game -> show generic activity, never invent a title. */ }
        _cache[placeId] = (result, DateTimeOffset.UtcNow);
        return result;
    }
}
