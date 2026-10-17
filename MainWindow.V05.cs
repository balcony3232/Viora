using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Viora;

// v0.5 additions are separated from the original working launcher / Discord plumbing.
public partial class MainWindow
{
    private string _lastSessionListKey = "";
    private DateTimeOffset _lastChartDrawn = DateTimeOffset.MinValue;
    private int _lastChartRange = -1;

    private sealed record Palette(string Background, string Sidebar, string Panel, string Raised,
        string Border, string Accent, string Hover, string Soft, string Muted);

    private Palette GetPalette(string name) => name == "Custom" ? CustomPalette() : name switch
    {
        "Amethyst" => new("#0D0915", "#110B1C", "#1A1327", "#2A1C40", "#443157", "#C29BFF", "#D5BAFF", "#624A86", "#B5A8C5"),
        "Midnight" => new("#090916", "#101024", "#17172E", "#262142", "#403457", "#B092FF", "#C7B0FF", "#594183", "#ADA7CB"),
        "Rose" => new("#130A15", "#1B101B", "#261827", "#38223A", "#533050", "#F38CCB", "#FFA8DB", "#794263", "#C4A8BD"),
        "Emerald" => new("#07120F", "#091A14", "#11241E", "#1D352B", "#345848", "#71DDB4", "#A3F0CE", "#2E624E", "#A3BDB0"),
        _ => new("#0B0912", "#100D1B", "#171321", "#241B39", "#332847", "#AE83FF", "#C19CFF", "#523180", "#A29CAF")
    };

    private Palette CustomPalette()
    {
        var c = _state.CustomTheme;
        var accent = c.Accent;
        return new Palette(c.Background, c.Sidebar, c.Panel,
            Mix(c.Panel, accent, 0.10), c.Border, accent, Mix(accent, "#FFFFFF", 0.13),
            Mix(c.Panel, accent, 0.26), c.Muted);
    }

    private static string Mix(string a, string b, double fraction)
    {
        var ca = (Color)ColorConverter.ConvertFromString(a);
        var cb = (Color)ColorConverter.ConvertFromString(b);
        byte lerp(byte x, byte y) => (byte)Math.Clamp((int)Math.Round(x * (1 - fraction) + y * fraction), 0, 255);
        return $"#{lerp(ca.R, cb.R):X2}{lerp(ca.G, cb.G):X2}{lerp(ca.B, cb.B):X2}";
    }

    private void InitializeV05()
    {
        // Existing settings are retained; new properties have backward-compatible defaults.
        var selected = AppearanceThemeCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(x => x.Tag?.ToString() == _state.ColorTheme);
        AppearanceThemeCombo.SelectedItem = selected ?? AppearanceThemeCombo.Items[0];
        AppearanceReduceMotion.IsChecked = _state.ReduceMotion;
        ApplyPalette(_state.ColorTheme);
        RefreshAvatarUI();
    }

    private static SolidColorBrush BrushFrom(string html)
        => new((Color)ColorConverter.ConvertFromString(html));

    private void ApplyPalette(string name)
    {
        var palette = GetPalette(name);
        var resources = Application.Current.Resources;
        resources["Bg"] = BrushFrom(palette.Background);
        resources["Sidebar"] = BrushFrom(palette.Sidebar);
        resources["Panel"] = BrushFrom(palette.Panel);
        resources["PanelRaised"] = BrushFrom(palette.Raised);
        resources["Stroke"] = BrushFrom(palette.Border);
        resources["Accent"] = BrushFrom(palette.Accent);
        resources["AccentHover"] = BrushFrom(palette.Hover);
        resources["AccentSoft"] = BrushFrom(palette.Soft);
        resources["Muted"] = BrushFrom(palette.Muted);
        _selectedNavBrush = BrushFrom(palette.Soft);
        _idleNavBrush = BrushFrom(palette.Raised);
        if (ThemeDescription is not null)
            ThemeDescription.Text = "Selected: " + name + " · Dark surfaces and accents follow your palette.";
        if (_ready)
        {
            SwitchPage(_currentPage);
            _lastChartRange = -1;
            RefreshWeeklyBars();
        }
    }

    private void AppearanceTheme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || AppearanceThemeCombo.SelectedItem is not ComboBoxItem item) return;
        var value = item.Tag?.ToString() ?? "Violet";
        _state.ColorTheme = value;
        ApplyPalette(value);
        Persist();
        SetStatus("Appearance theme changed to " + value + ".");
    }

    private void ReduceMotion_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _state.ReduceMotion = AppearanceReduceMotion.IsChecked == true;
        Persist();
        SetStatus(_state.ReduceMotion ? "Reduced motion enabled." : "Page animations enabled.");
    }

    private void AnimatePage(string page)
    {
        if (_state.ReduceMotion) return;
        FrameworkElement? target = page switch
        {
            "dashboard" => DashboardPage,
            "flags" => FlagsPage,
            "performance" => PerformancePage,
            "profiles" => ProfilesPage,
            "mods" => ModsPage,
            "stats" => StatsPage,
            "gamehub" => GameHubPage,
            "discord" => DiscordPage,
            "appearance" => AppearancePage,
            "splash" => SplashPage,
            "tools" => ToolsPage,
            "launcher" => LauncherPage,
            _ => null
        };
        if (target is null) return;
        target.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = 0.48,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void RefreshAvatarUI()
    {
        var path = _state.AvatarImageFile;
        bool available = !string.IsNullOrEmpty(path) && File.Exists(path);
        ImageBrush? picture = null;
        if (available)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 360;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                picture = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            }
            catch { available = false; }
        }
        SidebarAvatar.Fill = (Brush?)picture ?? BrushFrom("#332451");
        DashboardAvatar.Fill = (Brush?)picture ?? BrushFrom("#2C1B46");
        AppearanceAvatar.Fill = (Brush?)picture ?? BrushFrom("#332451");
        DashboardAvatarFallback.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        AvatarStatusText.Text = available ? "Custom avatar saved on this PC." : "Default Viora diamond (no custom image selected).";
    }

    private void ChooseAvatar_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Pictures (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", Title = "Choose a profile picture" };
        if (picker.ShowDialog() != true) return;
        try
        {
            var source = new FileInfo(picker.FileName);
            if (source.Length > 5 * 1024 * 1024) throw new InvalidDataException("Choose a picture smaller than 5 MB.");
            // Validate the image before copying it, without keeping the source file open.
            using (var stream = File.OpenRead(source.FullName))
                _ = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Directory.CreateDirectory(Storage.Root);
            string destination = Path.Combine(Storage.Root, "avatar" + source.Extension.ToLowerInvariant());
            if (!string.Equals(source.FullName, destination, StringComparison.OrdinalIgnoreCase))
                File.Copy(source.FullName, destination, true);
            _state.AvatarImageFile = destination;
            Persist();
            RefreshAvatarUI();
            SetStatus("Profile picture changed.");
        }
        catch (Exception ex) { Alert("Could not save profile picture: " + ex.Message); }
    }

    private void ResetAvatar_Click(object sender, RoutedEventArgs e)
    {
        _state.AvatarImageFile = "";
        Persist();
        RefreshAvatarUI();
        SetStatus("Default Viora profile picture restored.");
    }

    private void SaveCompletedObservedSession(DateTimeOffset end)
    {
        if (!_sawRobloxRunning) return;
        var game = _state.Discord.ShowGameNames
            ? (string.IsNullOrWhiteSpace(_state.Discord.ManualExperienceName)
                ? _currentGameName ?? ""
                : _state.Discord.ManualExperienceName)
            : "";
        _state.Stats.RecentSessions ??= new();
        _state.Stats.RecentSessions.Insert(0, new ObservedSession
        {
            StartedUtc = _observedSessionStart,
            EndedUtc = end,
            ObservedSeconds = Math.Max(0, (long)Math.Round(_currentSessionSeconds)),
            Experience = game
        });
        if (_state.Stats.RecentSessions.Count > 250)
            _state.Stats.RecentSessions.RemoveRange(250, _state.Stats.RecentSessions.Count - 250);
    }

    private void StatsRange_Changed(object sender, SelectionChangedEventArgs e)
    {
        _lastChartRange = -1;
        if (_ready) RefreshWeeklyBars();
    }

    private void RefreshEnhancedStats()
    {
        if (StatsToday is null || _state?.Stats?.DailyObservedSeconds is null) return;
        var daily = _state.Stats.DailyObservedSeconds;
        string Today(int offset) => DateTime.Today.AddDays(-offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        StatsToday.Text = DisplayDuration(daily.GetValueOrDefault(Today(0)));
        long week = Enumerable.Range(0, 7).Sum(i => daily.GetValueOrDefault(Today(i)));
        StatsThisWeek.Text = DisplayDuration(week);
        int streak = 0;
        // A day-in-progress without runtime yet does not end a streak from yesterday.
        for (int i = daily.GetValueOrDefault(Today(0)) > 0 ? 0 : 1; i < 366; i++)
        {
            if (daily.GetValueOrDefault(Today(i)) <= 0) break;
            streak++;
        }
        StatsStreak.Text = streak.ToString(CultureInfo.InvariantCulture);
        var sessions = _state.Stats.RecentSessions ?? new List<ObservedSession>();
        string key = sessions.Count + ":" + (sessions.Count > 0 ? sessions[0].StartedUtc.Ticks : 0);
        if (StatsSessionsGrid is not null && key != _lastSessionListKey)
        {
            StatsSessionsGrid.ItemsSource = sessions.ToList();
            _lastSessionListKey = key;
        }
    }

    private static string Csv(string value)
    {
        // Prevent spreadsheet programs treating untrusted game titles as formulas.
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private void ExportStats_Click(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog { FileName = "viora-statistics.csv", Filter = "CSV files (*.csv)|*.csv" };
        if (picker.ShowDialog() != true) return;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Viora v0.5; locally observed statistics");
            sb.AppendLine("Metric,Value");
            sb.AppendLine("Observed total seconds," + _state.Stats.ObservedPlaySeconds);
            sb.AppendLine("Observed sessions," + _state.Stats.ObservedSessions);
            sb.AppendLine("Launcher requests," + _state.Stats.LaunchRequests);
            sb.AppendLine();
            sb.AppendLine("Daily activity");
            sb.AppendLine("Local date,Observed seconds");
            foreach (var day in _state.Stats.DailyObservedSeconds.OrderBy(k => k.Key))
                sb.AppendLine(Csv(day.Key) + "," + day.Value.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();
            sb.AppendLine("Completed session history (collected from v0.5)");
            sb.AppendLine("Started UTC,Ended UTC,Observed seconds,Experience");
            foreach (var session in _state.Stats.RecentSessions ?? new())
                sb.AppendLine(string.Join(",", Csv(session.StartedUtc.ToString("O")), Csv(session.EndedUtc.ToString("O")),
                    session.ObservedSeconds.ToString(CultureInfo.InvariantCulture), Csv(session.Experience)));
            File.WriteAllText(picker.FileName, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            SetStatus("Statistics exported to " + picker.FileName);
        }
        catch (Exception ex) { Alert("Statistics export failed: " + ex.Message); }
    }
}
