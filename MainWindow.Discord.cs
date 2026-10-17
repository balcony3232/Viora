using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace Viora;

public partial class MainWindow
{
    private DiscordPipeClient _discordPipe = new();
    private readonly RobloxGameNames _robloxGameNames = new();
    private DateTimeOffset _observedSessionStart;
    private DateTimeOffset _currentGameStart;
    private DateTimeOffset _lastGameScan = DateTimeOffset.MinValue;
    private DateTimeOffset _lastDiscordSent = DateTimeOffset.MinValue;
    private long? _currentPlaceId;
    private string? _currentGameName;
    private bool _scanningGame;
    private int _gameScanEpoch;
    private bool _publishingDiscord;
    private DateTimeOffset? _browserStartUtc;
    private string _lastActivityKey = "";
    private string _discordConnectivity = "Disabled — enable this feature when you're ready.";

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Tick; }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, System.Text.StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    private static TimeSpan WindowsIdleTime()
    {
        try
        {
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (GetLastInputInfo(ref info))
                return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Tick));
        }
        catch { /* Not supported in every environment. */ }
        return TimeSpan.Zero;
    }

    private static bool IsRobloxForeground()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            if (pid == 0) return false;
            using var process = Process.GetProcessById((int)pid);
            return string.Equals(process.ProcessName, "RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool IsBrowsingRobloxInForeground()
    {
        // Active window *title* only. We do not read browser history, URLs, other tabs or cookies.
        try
        {
            var window = GetForegroundWindow();
            if (window == 0) return false;
            GetWindowThreadProcessId(window, out uint pid);
            if (pid == 0) return false;
            using var process = Process.GetProcessById((int)pid);
            var browser = process.ProcessName.ToLowerInvariant();
            if (browser is not ("chrome" or "msedge" or "firefox" or "brave" or "opera" or "opera_gx")) return false;
            var text = new System.Text.StringBuilder(512);
            _ = GetWindowText(window, text, text.Capacity);
            return text.ToString().Contains("Roblox", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void InitializeDiscordControls()
    {
        var option = _state.Discord;
        DiscordEnabled.IsChecked = option.Enabled;
        DiscordApplicationId.Text = option.ApplicationId;
        DiscordShowGame.IsChecked = option.ShowGameNames;
        DiscordShowAfk.IsChecked = option.ShowAfk;
        DiscordShowClosed.IsChecked = option.ShowWhenRobloxClosed;
        DiscordShowBrowsing.IsChecked = option.DetectRobloxBrowsing;
        DiscordIdleLabel.Text = option.IdleLabel;
        DiscordImageKey.Text = option.LargeImageKey;
        DiscordManualGame.Text = option.ManualExperienceName;
        foreach (var item in DiscordIdleMinutes.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag?.ToString() == option.IdleMinutes.ToString())
            { DiscordIdleMinutes.SelectedItem = item; break; }
        }
        if (DiscordIdleMinutes.SelectedIndex < 0) DiscordIdleMinutes.SelectedIndex = 1;
        RefreshDiscordPreview();
    }

    private void OpenDiscordPortal_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://discord.com/developers/applications") { UseShellExecute = true }); }
        catch (Exception ex) { Alert("Couldn't open Discord Developer Portal: " + ex.Message); }
    }

    private async void SaveDiscord_Click(object sender, RoutedEventArgs e)
    {
        var id = DiscordApplicationId.Text.Trim();
        bool enabled = DiscordEnabled.IsChecked == true;
        if (enabled && !Regex.IsMatch(id, @"^\d{15,25}$"))
        {
            Alert("Paste your Discord Application ID (15–25 digits). Don't paste a bot token or client secret.");
            return;
        }
        var oldId = _state.Discord.ApplicationId;
        bool previouslyEnabled = _state.Discord.Enabled;
        var previousNameVisibility = _state.Discord.ShowGameNames;
        _state.Discord = new DiscordOptions
        {
            Enabled = enabled, ApplicationId = id,
            ShowGameNames = DiscordShowGame.IsChecked == true,
            ShowAfk = DiscordShowAfk.IsChecked == true,
            ShowWhenRobloxClosed = DiscordShowClosed.IsChecked == true,
            DetectRobloxBrowsing = DiscordShowBrowsing.IsChecked == true,
            IdleMinutes = int.TryParse((DiscordIdleMinutes.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var value) ? value : 5,
            IdleLabel = string.IsNullOrWhiteSpace(DiscordIdleLabel.Text) ? "Away from keyboard" : DiscordIdleLabel.Text.Trim(),
            LargeImageKey = DiscordImageKey.Text.Trim(),
            ManualExperienceName = DiscordManualGame.Text.Trim()
        };
        if (previousNameVisibility != _state.Discord.ShowGameNames)
        {
            _currentPlaceId = null;
            _currentGameName = null;
            _lastGameScan = DateTimeOffset.MinValue;
        }
        Persist();
        _lastActivityKey = "";
        _lastDiscordSent = DateTimeOffset.MinValue;
        if (!enabled || (previouslyEnabled && oldId != id))
        {
            await _discordPipe.ClearIfConnectedAsync();
        }
        if (!enabled)
        {
            _discordConnectivity = "Disabled — presence cleared.";
            RefreshDiscordPreview();
            SetStatus("Discord presence disabled.");
            return;
        }
        // Force a new activity, even if Roblox is not running and 'show when closed' is disabled.
        await PublishDiscordNowAsync(force: true);
        SetStatus("Discord Rich Presence settings saved. Keep Discord desktop and Viora running.");
    }

    private async void DisableDiscord_Click(object sender, RoutedEventArgs e)
    {
        DiscordEnabled.IsChecked = false;
        _state.Discord.Enabled = false;
        Persist();
        _lastActivityKey = "";
        await _discordPipe.ClearIfConnectedAsync();
        _discordConnectivity = "Disabled — presence cleared.";
        RefreshDiscordPreview();
        SetStatus("Discord Rich Presence is off.");
    }

    private void OnRobloxMonitoringTick(bool running)
    {
        if (!running)
        {
            if (!_state.Discord.Enabled || !_state.Discord.DetectRobloxBrowsing || !IsBrowsingRobloxInForeground()) _browserStartUtc = null;
            else _browserStartUtc ??= DateTimeOffset.UtcNow;
            _currentPlaceId = null;
            _currentGameName = null;
            _currentGameStart = default;
            _lastGameScan = DateTimeOffset.MinValue;
            DashboardGameText.Text = "";
        }
        else if (!_scanningGame && DateTimeOffset.UtcNow - _lastGameScan >= TimeSpan.FromSeconds(12))
        {
            _lastGameScan = DateTimeOffset.UtcNow;
            _ = ScanGameAsync();
        }
        RefreshDiscordPreview();
        if (_state.Discord.Enabled) _ = PublishDiscordNowAsync();
    }

    private bool IsCurrentGameScan(int epoch, DateTimeOffset sessionStart) =>
        _isSignedIn && _sawRobloxRunning && _gameScanEpoch == epoch &&
        _observedSessionStart == sessionStart;

    private async Task ScanGameAsync()
    {
        if (_scanningGame) return;
        _scanningGame = true;
        int epoch = _gameScanEpoch;
        var sessionStart = _observedSessionStart;
        try
        {
            var foundId = await Task.Run(() => RobloxGameDetection.FindLatestPlaceId(sessionStart));
            // A lookup can finish after Roblox closes or a second process starts.
            if (!IsCurrentGameScan(epoch, sessionStart)) return;
            if (foundId is long id && id > 0 && id != _currentPlaceId)
            {
                _currentPlaceId = id;
                _currentGameStart = DateTimeOffset.UtcNow; // Detection time, not guaranteed join time.
                _currentGameName = null;
                RefreshDiscordPreview();
                string? resolvedName = await _robloxGameNames.GetNameAsync(id);
                // Never revive a previous game or update its label after a session ended.
                if (!IsCurrentGameScan(epoch, sessionStart) || _currentPlaceId != id) return;
                _currentGameName = resolvedName;
                UpdateGameName(id, resolvedName);
                _lastActivityKey = "";
                RefreshDiscordPreview();
                _ = PublishDiscordNowAsync(force: true);
            }
        }
        catch { /* Logs and public game API are best effort. */ }
        finally { _scanningGame = false; }
    }

    private DiscordActivity? CurrentDiscordActivity()
    {
        var option = _state.Discord;
        if (!option.Enabled) return null;
        if (!_sawRobloxRunning)
        {
            if (option.DetectRobloxBrowsing && IsBrowsingRobloxInForeground())
            {
                _browserStartUtc ??= DateTimeOffset.UtcNow;
                return new DiscordActivity
                {
                    Details = "Browsing Roblox",
                    State = "Exploring Roblox in a browser",
                    LargeImageKey = option.LargeImageKey,
                    StartUnixSeconds = _browserStartUtc.Value.ToUnixTimeSeconds()
                };
            }
            _browserStartUtc = null;
            return option.ShowWhenRobloxClosed ? new DiscordActivity
            {
                Details = "Roblox not running",
                State = "Idle in Viora",
                LargeImageKey = option.LargeImageKey
            } : null;
        }
        _browserStartUtc = null;
        string name = option.ShowGameNames && !string.IsNullOrWhiteSpace(option.ManualExperienceName)
            ? option.ManualExperienceName
            : option.ShowGameNames && !string.IsNullOrWhiteSpace(_currentGameName) ? _currentGameName : "Roblox";
        var idle = WindowsIdleTime();
        string state = option.ShowAfk && idle >= TimeSpan.FromMinutes(option.IdleMinutes)
            ? option.IdleLabel
            : IsRobloxForeground() ? "Playing" : "Roblox in the background";
        var overridePreset = option.ShowGameNames ? _state.GamePresencePresets.FirstOrDefault(p => p.PlaceId == _currentPlaceId && p.Enabled) : null;
        string details = name == "Roblox" ? "Playing Roblox" : "Playing " + name;
        if (overridePreset is not null && !string.IsNullOrWhiteSpace(overridePreset.Details))
            details = ExpandGameTemplate(overridePreset.Details, name);
        if (overridePreset is not null && !string.IsNullOrWhiteSpace(overridePreset.State) &&
            !(option.ShowAfk && idle >= TimeSpan.FromMinutes(option.IdleMinutes)))
            state = ExpandGameTemplate(overridePreset.State, name);
        return new DiscordActivity
        {
            Details = details,
            State = state,
            StartUnixSeconds = (_currentPlaceId.HasValue && option.ShowGameNames && _currentGameStart != default
                ? _currentGameStart : _observedSessionStart).ToUnixTimeSeconds(),
            LargeImageKey = !string.IsNullOrWhiteSpace(overridePreset?.LargeImageKey) ? overridePreset.LargeImageKey : option.LargeImageKey
        };
    }

    private void RefreshDiscordPreview()
    {
        if (DiscordPreviewDetails is null) return;
        var activity = CurrentDiscordActivity();
        DiscordPreviewDetails.Text = activity?.Details ?? "Rich Presence is off / hidden";
        DiscordPreviewState.Text = activity?.State ?? (_state.Discord.Enabled
            ? "Roblox is closed and idle presence is disabled" : "Enable Discord presence to start");
        DiscordPreviewTime.Text = activity?.StartUnixSeconds is long start
            ? "Session timer: " + DisplayDuration(Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - start))
            : "";
        DiscordConnectionStatus.Text = _discordConnectivity;
        if (DashboardGameText is not null)
            DashboardGameText.Text = _sawRobloxRunning && _state.Discord.ShowGameNames &&
                !string.IsNullOrWhiteSpace(_state.Discord.ManualExperienceName)
                ? "Custom experience label: " + _state.Discord.ManualExperienceName
                : _sawRobloxRunning && _state.Discord.ShowGameNames
                ? (_currentGameName is not null ? "Detected experience: " + _currentGameName
                   : _currentPlaceId is long id ? "Detected Roblox place #" + id + " (looking up its title)" : "Experience name not available yet")
                : "";
    }

    private async Task PublishDiscordNowAsync(bool force = false)
    {
        if (!_state.Discord.Enabled || _publishingDiscord || !_isSignedIn) return;
        var option = _state.Discord;
        if (!Regex.IsMatch(option.ApplicationId, @"^\d{15,25}$"))
        {
            _discordConnectivity = "Add a valid Discord Application ID, then save.";
            RefreshDiscordPreview();
            return;
        }
        var activity = CurrentDiscordActivity();
        string key = activity is null ? "clear" : string.Join("|", activity.Details, activity.State,
            activity.StartUnixSeconds?.ToString() ?? "", activity.LargeImageKey);
        var now = DateTimeOffset.UtcNow;
        if (!force && key == _lastActivityKey && now - _lastDiscordSent < TimeSpan.FromSeconds(60)) return;
        if (!force && now - _lastDiscordSent < TimeSpan.FromSeconds(12)) return;
        _publishingDiscord = true;
        _lastDiscordSent = now;
        try
        {
            await _discordPipe.PublishAsync(option.ApplicationId, activity);
            _lastActivityKey = key;
            _discordConnectivity = activity is null ? "Connected · Roblox is closed; activity is hidden." : "Connected · Activity sent to Discord desktop.";
        }
        catch (Exception ex)
        {
            _lastActivityKey = "";
            _discordConnectivity = "Not connected: " + ex.Message;
        }
        finally
        {
            _publishingDiscord = false;
            RefreshDiscordPreview();
        }
    }

    private void CloseDiscordSession()
    {
        _discordPipe.Dispose(); // Closing this pipe removes the local Discord activity.
        _discordPipe = new DiscordPipeClient(); // Allow reconnect after signing out and back in.
        _lastActivityKey = "";
        _lastDiscordSent = DateTimeOffset.MinValue;
        _discordConnectivity = "Disconnected.";
    }
}
