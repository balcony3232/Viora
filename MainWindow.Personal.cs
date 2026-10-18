using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Viora;

public partial class MainWindow
{
    private bool _isSignedIn;
    private string _currentUsername = "";
    private DispatcherTimer? _monitorTimer;
    private DateTimeOffset _lastMonitorTime;
    private DateTimeOffset _lastStatsSave;
    private double _currentSessionSeconds;
    private bool _sawRobloxRunning;
    private int? _activeRobloxProcessId;

    // A remaining background RobloxPlayerBeta process does not count as an active session.
    // Process.MainWindowHandle excludes headless/helper processes and still detects minimized windows.
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    private static int? FindOpenRobloxPlayer()
    {
        var processes = Process.GetProcessesByName("RobloxPlayerBeta");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    process.Refresh();
                    if (process.HasExited) continue;
                    nint window = process.MainWindowHandle;
                    if (window != 0 && IsWindowVisible(window)) return process.Id;
                }
                catch (InvalidOperationException) { /* The process exited while being examined. */ }
                catch (System.ComponentModel.Win32Exception) { /* Ignore inaccessible helper processes. */ }
            }
            return null;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    private void InitializePersonalFeatures()
    {
        // Explicit dark mode for the native title bar, as supported by Windows 10/11.
        SourceInitialized += (_, _) =>
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                int enable = 1;
                if (DwmSetWindowAttribute(handle, 20, ref enable, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, 19, ref enable, sizeof(int));
            }
            catch { /* Older Windows builds may not support dark caption bars. */ }
        };
        RefreshAuthenticationScreen();
        InitializeSplashEditor();
        RefreshStatsUI();
        InitializeDiscordControls();
        // Public builds require the account password at each new sign-in.
        // A plaintext stay-signed-in flag must not bypass local authentication.
    }

    private void RefreshAuthenticationScreen()
    {
        var existing = LocalAccountService.Exists;
        AuthTitle.Text = existing ? "Local account sign-in" : "Create local account";
        AuthSubtitle.Text = existing ? "Sign in to the existing local Viora profile." : "Set up a private profile on this Windows PC.";
        AuthConfirmPanel.Visibility = existing ? Visibility.Collapsed : Visibility.Visible;
        AuthGuest.Visibility = existing ? Visibility.Collapsed : Visibility.Visible;
        AuthSubmit.Content = existing ? "Sign in" : "Create local account";
        AuthUsername.Text = LocalAccountService.ExistingUsername() ?? "";
        AuthPassword.Clear();
        AuthConfirm.Clear();
        AuthError.Text = "";
         AuthOverlay.Visibility = Visibility.Visible;
    }

    private void AuthSubmit_Click(object sender, RoutedEventArgs e)
    {
        if (_isSignedIn) return;
        var username = AuthUsername.Text.Trim();
        var password = AuthPassword.Password;
        try
        {
            string? approvedUsername;
            if (!LocalAccountService.Exists)
            {
                if (password != AuthConfirm.Password)
                    throw new ArgumentException("The passwords don't match.");
                approvedUsername = LocalAccountService.Register(username, password);
            }
            else
            {
                approvedUsername = LocalAccountService.Authenticate(username, password);
                if (approvedUsername is null) throw new ArgumentException("Incorrect username or password.");
            }
             _ = FinishSignInAsync(approvedUsername);
        }
        catch (Exception ex)
        {
            AuthError.Text = ex.Message;
            AuthPassword.Clear();
            AuthConfirm.Clear();
        }
    }

    private void AuthGuest_Click(object sender, RoutedEventArgs e)
    {
        if (_isSignedIn || LocalAccountService.Exists) return;
        _ = FinishSignInAsync("Guest");
    }

    private void ShowFirstRunWizard(bool force = false)
    {
        if (!force && _state.SetupWizardCompleted) return;
        string preset = ActiveProfile.PerformancePreset.StartsWith("Maximum performance", StringComparison.OrdinalIgnoreCase)
            ? PerformancePresets.Max
            : ActiveProfile.PerformancePreset.StartsWith("Balanced", StringComparison.OrdinalIgnoreCase)
                ? PerformancePresets.Balanced : PerformancePresets.Default;
        string previousTheme = _state.ColorTheme;
        var wizard = new FirstRunSetupWindow(_state.ColorTheme, preset, _state.Discord.Enabled,
            _state.AppearanceStyle, (theme, style) => ApplyPalette(theme, style)) { Owner = this };
        if (wizard.ShowDialog() != true)
        {
            // Cancel must revert both the colour theme and the previous surface style.
            ApplyPalette(previousTheme);
            return;
        }

        _state.AppearanceStyle = wizard.SelectedStyle;
        AppearanceStyleCombo.SelectedItem = AppearanceStyleCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>()
            .FirstOrDefault(x => x.Tag?.ToString() == _state.AppearanceStyle) ?? AppearanceStyleCombo.Items[0];
        _state.ColorTheme = wizard.SelectedTheme;
        var theme = AppearanceThemeCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>()
            .FirstOrDefault(x => string.Equals(x.Tag?.ToString(), _state.ColorTheme, StringComparison.OrdinalIgnoreCase));
        if (theme is not null) AppearanceThemeCombo.SelectedItem = theme;
        ApplyPalette(_state.ColorTheme);

        // Presets are saved only; Roblox files are not modified by the wizard.
        try
        {
            PerformancePresets.Apply(ActiveProfile, wizard.SelectedPerformancePreset, false,
                _catalog.Where(x => x.IsAllowlisted).Select(x => x.Name).ToArray());
        }
        catch (InvalidOperationException)
        {
            PerformancePresets.Clear(ActiveProfile);
        }
        // Do not attempt a Discord connection until a valid Application ID has been configured.
        _state.Discord.Enabled = wizard.EnableDiscordPresence &&
            System.Text.RegularExpressions.Regex.IsMatch(_state.Discord.ApplicationId ?? "", @"^\d{15,25}$");
        DiscordEnabled.IsChecked = _state.Discord.Enabled;
        _state.SetupWizardCompleted = true;
        Persist();
        SetStatus("First-run preferences saved locally.");
    }

    private void OpenSetupWizard_Click(object sender, RoutedEventArgs e) => ShowFirstRunWizard(force: true);

    private async Task FinishSignInAsync(string username)
    {
        _currentUsername = username;
        _isSignedIn = true;
        GreetingText.Text = "Hello, " + username + ".";
        SidebarAccountLabel.Text = "●  " + username;
        AuthPassword.Clear();
        AuthConfirm.Clear();
        AuthOverlay.Visibility = Visibility.Collapsed;
        SwitchPage("dashboard");
        StartMonitor();
        await ShowSplashAsync();
        if (!_state.SetupWizardCompleted) ShowFirstRunWizard();
        SetStatus("Local workspace ready · " + username);
        if (_state.Updates.CheckAtStartup && !string.IsNullOrWhiteSpace(_state.Updates.GitHubRepository))
            _ = CheckForUpdatesAsync(silent: true);
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        if (!_isSignedIn) return;
        StopMonitor();
        CloseDiscordSession();
        // Persistent auto sign-in is disabled; signing out only ends this session.
        _isSignedIn = false;
        _currentUsername = "";
        RefreshAuthenticationScreen();
    }

    private void StartMonitor()
    {
        if (_monitorTimer is not null) return;
        _lastMonitorTime = DateTimeOffset.UtcNow;
        _lastStatsSave = _lastMonitorTime;
        _sawRobloxRunning = false;
        _activeRobloxProcessId = null;
        _gameScanEpoch++;
        _currentSessionSeconds = 0;
        _observedSessionStart = DateTimeOffset.UtcNow;
        ResetGameTracking();
        _currentPlaceId = null;
        _currentGameName = null;
        _currentGameStart = default;
        _lastGameScan = DateTimeOffset.MinValue;
        _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _monitorTimer.Tick += (_, _) => MonitorRobloxProcess();
        _monitorTimer.Start();
        MonitorRobloxProcess();
    }

    private void StopMonitor()
    {
        _monitorTimer?.Stop();
        _monitorTimer = null;
        EndObservedRobloxSession(DateTimeOffset.UtcNow);
        Storage.Save(_state);
        RefreshStatsUI();
    }

    // Finalize exactly once, invalidate pending game-name lookups, and clear the live game.
    private void EndObservedRobloxSession(DateTimeOffset end)
    {
        if (_sawRobloxRunning)
        {
            FinalizeGameTracking(end);
            SaveCompletedObservedSession(end);
            _state.Stats.LongestObservedSessionSeconds = Math.Max(
                _state.Stats.LongestObservedSessionSeconds, (long)_currentSessionSeconds);
            // Persist the finished segment immediately rather than waiting for the 30s interval.
            Storage.Save(_state);
        }
        _sawRobloxRunning = false;
        _activeRobloxProcessId = null;
        _currentSessionSeconds = 0;
        _currentPlaceId = null;
        _currentGameName = null;
        _currentGameStart = default;
        _lastGameScan = DateTimeOffset.MinValue;
        _gameScanEpoch++;
        _lastActivityKey = "";
        _lastDiscordSent = DateTimeOffset.MinValue;
    }

    private void MonitorRobloxProcess()
    {
        if (!_isSignedIn) return;
        var now = DateTimeOffset.UtcNow;
        int? activeProcessId;
        try { activeProcessId = FindOpenRobloxPlayer(); }
        catch
        {
            // A failed process query must never cause a false start/stop or count the gap.
            _lastMonitorTime = now;
            return;
        }
        bool running = activeProcessId.HasValue;
        // Roblox can close and reopen between two 5-second polls. Process identity
        // prevents carrying the previous game's place ID into the new process.
        if (running && _sawRobloxRunning && _activeRobloxProcessId != activeProcessId)
            EndObservedRobloxSession(now);

        if (running)
        {
            if (!_sawRobloxRunning)
            {
                _sawRobloxRunning = true;
                _activeRobloxProcessId = activeProcessId;
                _currentSessionSeconds = 0;
                _observedSessionStart = now;
                _state.Stats.ObservedSessions++;
                _state.Stats.LastObservedStartUtc = now;
            }
            else
            {
                // Avoid claiming the PC's entire sleep interval as gaming time.
                var elapsed = Math.Clamp((now - _lastMonitorTime).TotalSeconds, 0, 15);
                _currentSessionSeconds += elapsed;
                long observed = (long)Math.Round(elapsed);
                _state.Stats.ObservedPlaySeconds += observed;
                string day = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                _state.Stats.DailyObservedSeconds.TryGetValue(day, out long total);
                _state.Stats.DailyObservedSeconds[day] = total + observed;
                _state.Stats.LongestObservedSessionSeconds = Math.Max(
                    _state.Stats.LongestObservedSessionSeconds, (long)_currentSessionSeconds);
                TrackGameObservation(now, observed);
            }
        }
        else if (_sawRobloxRunning)
        {
            EndObservedRobloxSession(now);
        }

        _lastMonitorTime = now;
        RefreshStatsUI();
        OnRobloxMonitoringTick(running);
        UpdateGameTrackingIndicator();
        RefreshGameHub();
        if ((now - _lastStatsSave).TotalSeconds >= 30)
        {
            Storage.Save(_state);
            _lastStatsSave = now;
        }
    }

    private static string DisplayDuration(long seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalHours >= 1 ? $"{(long)duration.TotalHours}h {duration.Minutes}m"
            : duration.TotalMinutes >= 1 ? $"{(long)duration.TotalMinutes}m {duration.Seconds}s"
            : $"{duration.Seconds}s";
    }

    private void RefreshStatsUI()
    {
        if (StatsPlaytime is null || _state.Stats is null) return;
        StatsPlaytime.Text = DisplayDuration(_state.Stats.ObservedPlaySeconds);
        StatsLongest.Text = DisplayDuration(_state.Stats.LongestObservedSessionSeconds);
        StatsSessions.Text = _state.Stats.ObservedSessions.ToString();
        StatsLaunches.Text = _state.Stats.LaunchRequests.ToString();
        StatsCurrentSession.Text = _sawRobloxRunning
            ? "●  Roblox is running  ·  Current observation: " + DisplayDuration((long)_currentSessionSeconds)
            : "○  No Roblox Player process detected";
        StatsLastSeen.Text = _state.Stats.LastObservedStartUtc is DateTimeOffset start
            ? "Most recent session first seen: " + start.ToLocalTime().ToString("dd MMM yyyy, HH:mm")
            : "No sessions seen yet.";
        if (DashboardActivityText is not null)
            DashboardActivityText.Text = _sawRobloxRunning ? "● Roblox running · tracking this session" : "○ No active Roblox session detected";
        RefreshWeeklyBars();
        RefreshEnhancedStats();
        RefreshGameHub();
    }

    private void RefreshWeeklyBars()
    {
        if (StatsWeekBars is null) return;
        var range = int.TryParse((StatsRangeCombo?.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString(), out var count) ? count : 7;
        // Redrawing thirty columns every five seconds disrupts scrolling, so update every thirty seconds.
        if (range == _lastChartRange && DateTimeOffset.UtcNow - _lastChartDrawn < TimeSpan.FromSeconds(30)) return;
        _lastChartRange = range;
        _lastChartDrawn = DateTimeOffset.UtcNow;
        StatsWeekBars.Children.Clear();
        var daily = _state.Stats.DailyObservedSeconds;
        var start = DateTime.Today.AddDays(1 - range);
        var days = Enumerable.Range(0, range).Select(i => start.AddDays(i)).ToArray();
        long max = Math.Max(60, days.Max(day => daily.GetValueOrDefault(day.ToString("yyyy-MM-dd"))));
        foreach (var day in days)
        {
            long seconds = daily.GetValueOrDefault(day.ToString("yyyy-MM-dd"));
            double height = Math.Max(seconds > 0 ? 4 : 1, Math.Min(82, 82.0 * seconds / max));
            var column = new System.Windows.Controls.StackPanel { Width = range == 30 ? 47 : 72, VerticalAlignment = VerticalAlignment.Bottom };
            var value = new System.Windows.Controls.TextBlock
            {
                Text = seconds == 0 ? "–" : DisplayDuration(seconds),
                Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 10, Margin = new Thickness(0, 0, 0, 4)
            };
            var bar = new System.Windows.Controls.Border
            {
                Background = (System.Windows.Media.Brush)Application.Current.Resources["Accent"],
                CornerRadius = new CornerRadius(4, 4, 0, 0), Height = height, Width = range == 30 ? 27 : 42,
                HorizontalAlignment = HorizontalAlignment.Center, ToolTip = day.ToString("dddd") + ": " + DisplayDuration(seconds)
            };
            var label = new System.Windows.Controls.TextBlock
            {
                Text = day.ToString("ddd"),
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(162, 156, 175)),
                FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0)
            };
            column.Children.Add(value);
            column.Children.Add(bar);
            column.Children.Add(label);
            StatsWeekBars.Children.Add(column);
        }
    }

    private void ResetStats_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Reset all statistics stored by Viora? This can't be undone.",
            "Reset statistics", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _state.Stats = new ActivityStats();
        _state.Stats.RecentSessions = new();
        ResetGameTracking();
        
        _lastChartRange = -1;
        if (_sawRobloxRunning)
        {
            _state.Stats.ObservedSessions = 1;
            _state.Stats.LastObservedStartUtc = DateTimeOffset.UtcNow;
            _currentSessionSeconds = 0;
        }
        Persist();
        RefreshStatsUI();
        SetStatus("Statistics reset.");
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        try
        {
            if (_monitorTimer is not null) StopMonitor();
            CloseDiscordSession();
        }
        catch { /* Never block closing if a disk is temporarily unavailable. */ }
        finally { DisposeTray(); }
    }
}
