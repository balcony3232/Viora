using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VioraManager;

/// <summary>
/// Local Discord IPC presence for this Manager process. A Discord Application ID
/// is required, never a bot token/secret. Can reuse the user's configured Viora
/// Application ID from %LOCALAPPDATA%\Viora\settings.json, read-only.
/// </summary>
internal sealed class ManagerDiscordPresence : IDisposable
{
    private sealed class Options
    {
        public bool Enabled { get; set; } = true;
        public string ApplicationId { get; set; } = "";
        public bool IncludeGpuEstimate { get; set; } = true;
    }

    private static readonly string PreferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Viora", "Manager", "discord-rpc.json");
    private static readonly string SharedSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Viora", "settings.json");

    private readonly DiscordPipeClient _client = new();
    private readonly DateTimeOffset _sessionStart = DateTimeOffset.UtcNow;
    private Options _options = new();
    private string _lastKey = "";
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private bool _sending;
    private bool _disposed;

    public bool Enabled => _options.Enabled;
    public string ApplicationId => _options.ApplicationId;
    public bool IncludeGpuEstimate => _options.IncludeGpuEstimate;
    public string Status { get; private set; } = "Initialising...";
    public bool UsingSharedAppId { get; private set; }

    public ManagerDiscordPresence()
    {
        try
        {
            if (File.Exists(PreferencesPath))
            {
                var stored = JsonSerializer.Deserialize<Options>(File.ReadAllText(PreferencesPath));
                _options = stored ?? new Options();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Status = "Discord settings could not be read; using defaults.";
        }
        if (!IsValidAppId(_options.ApplicationId))
        {
            string? inherited = FindSharedAppId();
            if (!string.IsNullOrWhiteSpace(inherited))
            {
                _options.ApplicationId = inherited;
                UsingSharedAppId = true;
            }
        }
        RefreshIdleStatus();
    }

    private static string? FindSharedAppId()
    {
        try
        {
            if (!File.Exists(SharedSettingsPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(SharedSettingsPath));
            if (!doc.RootElement.TryGetProperty("Discord", out var discord)) return null;
            if (!discord.TryGetProperty("ApplicationId", out var value) ||
                value.ValueKind != JsonValueKind.String) return null;
            string id = value.GetString()?.Trim() ?? "";
            return IsValidAppId(id) ? id : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static bool IsValidAppId(string? id) =>
        id is not null && Regex.IsMatch(id, @"^\d{15,25}$");

    public void SaveSettings(bool enabled, string appId, bool includeGpu)
    {
        if (enabled && !IsValidAppId(appId))
            throw new ArgumentException(
                "Enter a 15–25 digit Discord Application ID, not a bot token or client secret.");

        var oldId = _options.ApplicationId;
        _options = new Options
        {
            Enabled = enabled, ApplicationId = appId.Trim(),
            IncludeGpuEstimate = includeGpu
        };
        UsingSharedAppId = false;
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
        File.WriteAllText(PreferencesPath,
            JsonSerializer.Serialize(_options, new JsonSerializerOptions { WriteIndented = true }));

        _lastAttempt = DateTimeOffset.MinValue;
        _lastKey = "";
        if (!enabled || !string.Equals(oldId, appId.Trim(), StringComparison.Ordinal))
            _ = ClearAsync();
        RefreshIdleStatus();
    }

    private async Task ClearAsync()
    {
        try { await _client.ClearIfConnectedAsync(); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException
                                   or ObjectDisposedException)
        {
            // Not connected or Discord closed. No activity to clear locally.
        }
    }

    private void RefreshIdleStatus()
    {
        if (!_options.Enabled) Status = "Off — Rich Presence disabled.";
        else if (!IsValidAppId(_options.ApplicationId))
            Status = "Add your Discord Application ID in Discord RPC settings.";
        else if (UsingSharedAppId)
            Status = "Using Viora's saved Discord Application ID — connecting when Discord desktop is open.";
        else Status = "Ready — connecting to Discord desktop...";
    }

    public async Task UpdateAsync(int instanceCount, double cpuPercent, double? gpuPercent)
    {
        if (_disposed || !_options.Enabled || !IsValidAppId(_options.ApplicationId) || _sending)
            return;

        var now = DateTimeOffset.UtcNow;
        string title = instanceCount == 1
            ? "Managing 1 Roblox instance" : "Managing " + instanceCount + " Roblox instances";
        string state = "Roblox CPU " + Math.Clamp(cpuPercent, 0, 100).ToString("0",
            System.Globalization.CultureInfo.InvariantCulture) + "%";
        if (_options.IncludeGpuEstimate)
            state += gpuPercent.HasValue
                ? " | 3D GPU ~" + gpuPercent.Value.ToString("0",
                    System.Globalization.CultureInfo.InvariantCulture) + "%"
                : " | GPU unavailable";
        string key = title + "|" + state + "|" + _options.ApplicationId;
        // Respect Discord activity update limits; retry connection failures.
        if (now - _lastAttempt < TimeSpan.FromSeconds(15)) return;
        if (key == _lastKey && now - _lastAttempt < TimeSpan.FromSeconds(60)) return;
        _lastAttempt = now;
        _sending = true;
        try
        {
            await _client.PublishAsync(_options.ApplicationId, new DiscordActivity
            {
                Details = title,
                State = state,
                StartUnixSeconds = _sessionStart.ToUnixTimeSeconds()
            });
            _lastKey = key;
            Status = "Connected — " + title + " | " + state;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException
                                   or ObjectDisposedException)
        {
            _lastKey = "";
            Status = "Discord unavailable: " + ex.Message;
        }
        finally { _sending = false; }
    }

    public void Dispose()
    {
        _disposed = true;
        _client.Dispose();
    }
}
