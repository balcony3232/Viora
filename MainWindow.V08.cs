using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Viora;

public partial class MainWindow
{
    private bool _initializingV08;
    private System.Windows.Forms.NotifyIcon? _tray;
    private UpdateRelease? _latestRelease;
    private bool _checkingUpdates;
    private bool _downloadingUpdate;

    private void InitializeV08()
    {
        _initializingV08 = true;
        UpdatesRepo.Text = _state.Updates.GitHubRepository;
        UpdatesAtStartup.IsChecked = _state.Updates.CheckAtStartup;
        UpdatesIncludeManager.IsChecked = false;
        UpdatesIncludeManager.IsEnabled = false;
        TrayEnabled.IsChecked = _state.MinimizeToTray;
        QuickProfile.ItemsSource = _state.Profiles;
        QuickFps.SelectedIndex = 0;
        QuickQuality.SelectedIndex = 0;
        LoadThemeFields();
        RefreshQuickList();
        InitializeTray();
        _initializingV08 = false;
    }

    private void RefreshQuickList()
    {
        if (QuickList is null) return;
        string selected = (QuickList.SelectedItem as LaunchShortcut)?.Id ?? "";
        string filter = QuickSearch?.Text?.Trim() ?? "";
        QuickList.ItemsSource = _state.LaunchShortcuts
            .Where(q => (q.Name + " " + q.Folder + " " + q.PlaceId).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(q => q.Folder, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(q => q.LastLaunchedUtc)
            .ThenBy(q => q.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (!string.IsNullOrWhiteSpace(selected))
            QuickList.SelectedItem = (QuickList.ItemsSource as IEnumerable<LaunchShortcut>)?.FirstOrDefault(q => q.Id == selected);
    }
    private static long ParsePlace(string text)
    {
        text = text.Trim();
        if (long.TryParse(text, out long number) && number > 0) return number;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var url) || url.Scheme != "https" ||
            !(url.Host.Equals("www.roblox.com", StringComparison.OrdinalIgnoreCase) ||
              url.Host.Equals("roblox.com", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Use a positive Roblox place ID or an official https://www.roblox.com/games/... URL.");
        var match = Regex.Match(url.AbsolutePath, @"^/games/(\d+)(?:/|$)", RegexOptions.IgnoreCase);
        if (!match.Success || !long.TryParse(match.Groups[1].Value, out number) || number <= 0)
            throw new ArgumentException("Roblox game URL must contain a valid /games/PLACE_ID path.");
        return number;
    }
    private void QuickSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var id = ParsePlace(QuickPlace.Text);
            if (_state.LaunchShortcuts.Any(q => q.PlaceId == id))
                throw new InvalidOperationException("This place is already saved. Select it to update its settings.");
            var entry = new LaunchShortcut { PlaceId = id, Name = QuickName.Text.Trim(),
                Folder = string.IsNullOrWhiteSpace(QuickFolder.Text) ? "Games" : QuickFolder.Text.Trim(),
                ProfileId = (QuickProfile.SelectedItem as UserProfile)?.Id ?? "",
                Fps = SelectedFps(), Quality = SelectedQuality() };
            _state.LaunchShortcuts.Add(entry);
            Persist(); RefreshQuickList(); QuickList.SelectedItem = entry;
            SetStatus("Saved '" + entry.DisplayName + "' to Quick Launcher.");
        }
        catch (Exception ex) { Alert("Could not save game: " + ex.Message); }
    }
    private void QuickUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (QuickList.SelectedItem is not LaunchShortcut entry) { SetStatus("Select a saved game first."); return; }
        try
        {
            var id = ParsePlace(QuickPlace.Text);
            if (_state.LaunchShortcuts.Any(q => q.Id != entry.Id && q.PlaceId == id))
                throw new ArgumentException("Another shortcut already has this place ID.");
            entry.PlaceId = id;
            entry.Name = QuickName.Text.Trim();
            entry.Folder = string.IsNullOrWhiteSpace(QuickFolder.Text) ? "Games" : QuickFolder.Text.Trim();
            entry.ProfileId = (QuickProfile.SelectedItem as UserProfile)?.Id ?? "";
            entry.Fps = SelectedFps(); entry.Quality = SelectedQuality();
            Persist(); RefreshQuickList(); QuickList.SelectedItem = entry;
            SetStatus("Updated game and performance assignment.");
        }
        catch (Exception ex) { Alert("Could not update game: " + ex.Message); }
    }
    private static void SetComboTag(ComboBox combo, int number)
    {
        var item = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => int.TryParse(i.Tag?.ToString(), out var v) && v == number);
        combo.SelectedItem = item ?? combo.Items[0];
    }
    private int SelectedFps() => int.TryParse((QuickFps.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var v) ? v : 0;
    private int SelectedQuality() => int.TryParse((QuickQuality.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var v) ? v : -1;
    private void QuickList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QuickList.SelectedItem is not LaunchShortcut q) return;
        QuickName.Text = q.Name; QuickPlace.Text = q.PlaceId.ToString(); QuickFolder.Text = q.Folder;
        QuickProfile.SelectedItem = _state.Profiles.FirstOrDefault(p => p.Id == q.ProfileId);
        SetComboTag(QuickFps, q.Fps); SetComboTag(QuickQuality, q.Quality);
    }
    private void QuickSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (_ready) RefreshQuickList();
    }
    private void QuickRemove_Click(object sender, RoutedEventArgs e)
    {
        if (QuickList.SelectedItem is not LaunchShortcut q) return;
        if (MessageBox.Show("Remove the quick-launch entry for '" + q.DisplayName + "'? Game history will not be deleted.",
            "Remove shortcut", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _state.LaunchShortcuts.Remove(q);
        Persist(); RefreshQuickList();
    }
    private void QuickCopy_Click(object sender, RoutedEventArgs e)
    {
        if (QuickList.SelectedItem is not LaunchShortcut q) return;
        try { Clipboard.SetText($"https://www.roblox.com/games/{q.PlaceId}/"); SetStatus("Official Roblox game URL copied."); }
        catch (Exception ex) { Alert("Copy failed: " + ex.Message); }
    }
    private void QuickLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (QuickList.SelectedItem is not LaunchShortcut q) { SetStatus("Choose a saved game."); return; }
        try
        {
            bool needsProfile = !string.IsNullOrWhiteSpace(q.ProfileId);
            bool needsGraphics = q.Fps > 0 || q.Quality >= 0;
            if (needsProfile || needsGraphics)
            {
                if (Process.GetProcessesByName("RobloxPlayerBeta").Length != 0)
                    throw new InvalidOperationException("Close Roblox before opening a game with a performance profile.");
                if (MessageBox.Show("Apply the assigned settings before opening '" + q.DisplayName + "'?\n\nRoblox may ignore some locally saved preferences.",
                    "Quick launch", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                if (needsProfile)
                {
                    var profile = _state.Profiles.FirstOrDefault(p => p.Id == q.ProfileId)
                        ?? throw new InvalidOperationException("The assigned profile was deleted. Edit this shortcut to choose another.");
                    if (SelectedInstallation is null) throw new InvalidOperationException("Roblox installation was not detected.");
                    _state.ActiveProfileId = profile.Id;
                    if (!ApplyCurrentProfile()) return;
                }
                if (needsGraphics)
                {
                    var previous = RobloxGraphicsSettings.Read();
                    int fps = q.Fps > 0 ? q.Fps : previous.Fps ?? throw new InvalidOperationException("Saved FPS value unavailable. Choose an FPS cap explicitly.");
                    int quality = q.Quality >= 0 ? q.Quality : previous.Quality ?? throw new InvalidOperationException("Saved graphics quality unavailable. Choose a quality value explicitly.");
                    RobloxGraphicsSettings.Apply(fps, quality);
                }
            }
            RobloxService.OpenUrl($"https://www.roblox.com/games/{q.PlaceId}/");
            q.LastLaunchedUtc = DateTimeOffset.UtcNow;
            q.LaunchRequests++;
            _state.Stats.LaunchRequests++;
            Persist(); RefreshQuickList();
            SetStatus("Opened the official game page. A requested launch is not a confirmed game join.");
        }
        catch (Exception ex) { Alert("Quick launch failed: " + ex.Message); }
    }

    private void LoadThemeFields()
    {
        var t = _state.CustomTheme;
        ThemeBackground.Text = t.Background; ThemeSidebar.Text = t.Sidebar; ThemePanel.Text = t.Panel;
        ThemeAccent.Text = t.Accent; ThemeBorder.Text = t.Border; ThemeMuted.Text = t.Muted;
    }
    private void ThemeSave_Click(object sender, RoutedEventArgs e)
    {
        var t = new ThemeColours { Background = ThemeBackground.Text.Trim(), Sidebar = ThemeSidebar.Text.Trim(),
            Panel = ThemePanel.Text.Trim(), Accent = ThemeAccent.Text.Trim(), Border = ThemeBorder.Text.Trim(), Muted = ThemeMuted.Text.Trim() };
        var fields = new[] { t.Background, t.Sidebar, t.Panel, t.Accent, t.Border, t.Muted };
        if (fields.Any(c => !Regex.IsMatch(c, @"^#[a-fA-F0-9]{6}$")))
        { Alert("Each colour must be in #RRGGBB format, e.g. #AE83FF."); return; }
        _state.CustomTheme = t;
        _state.ColorTheme = "Custom";
        AppearanceThemeCombo.SelectedItem = AppearanceThemeCombo.Items.OfType<ComboBoxItem>().First(x => x.Tag?.ToString() == "Custom");
        ApplyPalette("Custom"); Persist();
        SetStatus("Custom palette saved. Existing themes remain available.");
    }
    private static string PickColour(string existing)
    {
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
        try { dialog.Color = System.Drawing.ColorTranslator.FromHtml(existing); }
        catch { dialog.Color = System.Drawing.Color.MediumPurple; }
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return existing;
        return $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    }
    private void ThemePickAccent_Click(object sender, RoutedEventArgs e)
        => ThemeAccent.Text = PickColour(ThemeAccent.Text);
    private void ThemePickBackground_Click(object sender, RoutedEventArgs e)
        => ThemeBackground.Text = PickColour(ThemeBackground.Text);
    private void ThemeExport_Click(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog { Filter = "Viora theme (*.json)|*.json", FileName = "viora-custom-theme.json" };
        if (picker.ShowDialog() != true) return;
        try { Storage.WriteJson(picker.FileName, _state.CustomTheme); SetStatus("Custom theme exported."); }
        catch (Exception ex) { Alert("Could not export theme: " + ex.Message); }
    }
    private void ThemeImport_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Viora theme (*.json)|*.json" };
        if (picker.ShowDialog() != true) return;
        try
        {
            if (new FileInfo(picker.FileName).Length > 32_000) throw new InvalidDataException("Theme file is too large.");
            var imported = System.Text.Json.JsonSerializer.Deserialize<ThemeColours>(File.ReadAllText(picker.FileName), Storage.JsonOptions)
                ?? throw new InvalidDataException("Invalid theme data.");
            var fields = new[] { imported.Background, imported.Sidebar, imported.Panel, imported.Accent, imported.Border, imported.Muted };
            if (fields.Any(c => c is null || !Regex.IsMatch(c, @"^#[a-fA-F0-9]{6}$")))
                throw new InvalidDataException("Theme has missing or invalid hex colours.");
            _state.CustomTheme = imported;
            LoadThemeFields();
            _state.ColorTheme = "Custom";
            AppearanceThemeCombo.SelectedItem = AppearanceThemeCombo.Items.OfType<ComboBoxItem>().First(x => x.Tag?.ToString() == "Custom");
            ApplyPalette("Custom"); Persist();
            SetStatus("Custom theme imported and applied.");
        }
        catch (Exception ex) { Alert("Could not import theme: " + ex.Message); }
    }
    private void ThemeReset_Click(object sender, RoutedEventArgs e)
    {
        _state.CustomTheme = new ThemeColours(); LoadThemeFields();
        if (_state.ColorTheme == "Custom") ApplyPalette("Custom");
        Persist(); SetStatus("Custom colours reset to Viora defaults.");
    }

    private void UpdatesSave_Click(object sender, RoutedEventArgs e)
    {
        string repo = UpdatesRepo.Text.Trim();
        if (!string.IsNullOrWhiteSpace(repo) && !ReleaseUpdateService.TryNormalizeRepo(repo, out repo))
        { Alert("Enter owner/repo or an official GitHub repository URL."); return; }
        if (string.IsNullOrWhiteSpace(repo) && UpdatesAtStartup.IsChecked == true)
        { Alert("Configure a repository before enabling startup checks."); return; }
        _state.Updates.GitHubRepository = repo;
        _state.Updates.CheckAtStartup = UpdatesAtStartup.IsChecked == true;
        _latestRelease = null; UpdatesDownload.IsEnabled = false;
        Persist(); UpdatesResult.Text = "Update source saved. Click Check for updates.";
    }
    private static string? InstalledManagerPath()
    {
        string? current = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(current)) return null;
        string candidate = Path.Combine(Path.GetDirectoryName(current)!, "VioraManager.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    private static bool ManagerUpdateNeeded(string targetVersion)
    {
        var path = InstalledManagerPath();
        if (path is null || !Version.TryParse(targetVersion, out var latest)) return false;
        try
        {
            var installed = FileVersionInfo.GetVersionInfo(path).FileVersion;
            return !Version.TryParse(installed, out var current) || current < latest;
        }
        catch { return false; }
    }

    private void RefreshUpdateOffer()
    {
        if (_latestRelease is null) { UpdatesDownload.IsEnabled = false; return; }
        bool mainNew = ReleaseUpdateService.IsNewer(_latestRelease.Version);
        bool managerNew = UpdatesIncludeManager.IsChecked == true && UpdatesIncludeManager.IsEnabled;
        UpdatesDownload.IsEnabled = mainNew || managerNew;
    }

    private void UpdatesIncludeManager_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initializingV08) RefreshUpdateOffer();
    }

    private async void UpdatesCheck_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync();
    private async Task CheckForUpdatesAsync(bool silent = false)
    {
        if (_checkingUpdates || !_isSignedIn) return;
        _checkingUpdates = true;
        UpdatesDownload.IsEnabled = false;
        UpdatesIncludeManager.IsEnabled = false;
        UpdatesResult.Text = "Checking published GitHub Releases…";
        try
        {
            _latestRelease = await ReleaseUpdateService.GetLatestAsync(_state.Updates.GitHubRepository);
            bool mainNew = ReleaseUpdateService.IsNewer(_latestRelease.Version);
            bool managerNew = _latestRelease.SelectManager() is not null &&
                ManagerUpdateNeeded(_latestRelease.Version);
            UpdatesIncludeManager.IsEnabled = managerNew;
            UpdatesIncludeManager.IsChecked = managerNew;
            RefreshUpdateOffer();

            var launcherAsset = _latestRelease.SelectLauncher();
            bool lite = launcherAsset.FileName.Contains("-Lite.exe", StringComparison.OrdinalIgnoreCase);
            UpdatesPayloadInfo.Text = lite
                ? "The smaller .NET Desktop Runtime 8 package is available. Launcher and Manager are downloaded separately."
                : ReleaseUpdateService.HasDesktopRuntime8
                    ? "This release has no lightweight Launcher file; using a full self-contained download."
                    : ".NET Desktop Runtime 8 (x64) was not detected; using a full self-contained download. No runtime is installed automatically.";
            if (managerNew) UpdatesPayloadInfo.Text += " A Manager update is also available (close Manager before installing).";
            UpdatesResult.Text = mainNew || managerNew
                ? "Viora v" + _latestRelease.Version + " is available. Confirm your selected components to continue."
                : "Up to date. Latest published release: v" + _latestRelease.Version + ".";
            if (silent && (mainNew || managerNew))
                SetStatus("Viora v" + _latestRelease.Version + " is available in Utilities.");
        }
        catch (Exception ex)
        {
            _latestRelease = null;
            UpdatesIncludeManager.IsChecked = false;
            UpdatesIncludeManager.IsEnabled = false;
            UpdatesResult.Text = "Couldn't check for updates: " + ex.Message;
            if (!silent) Alert(UpdatesResult.Text);
        }
        finally { _checkingUpdates = false; }
    }

    private async void UpdatesDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadingUpdate || _latestRelease is null) return;
        if (!ReleaseUpdateService.TryNormalizeRepo(_state.Updates.GitHubRepository, out var repo) ||
            !repo.Equals("balcony3232/Viora", StringComparison.OrdinalIgnoreCase))
        {
            Alert("One-click installation is supported only for the official balcony3232/Viora release feed.");
            return;
        }

        bool updateMain = ReleaseUpdateService.IsNewer(_latestRelease.Version);
        bool updateManager = UpdatesIncludeManager.IsChecked == true &&
            UpdatesIncludeManager.IsEnabled && ManagerUpdateNeeded(_latestRelease.Version);
        if (!updateMain && !updateManager) return;

        if (updateManager)
        {
            var processes = Process.GetProcessesByName("VioraManager");
            try
            {
                if (processes.Any(p => { try { return !p.HasExited; } catch { return false; } }))
                {
                    Alert("Close Viora Manager before updating it. Reopen it once Viora restarts.");
                    return;
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }

        UpdateAsset? managerAsset = updateManager ? _latestRelease.SelectManager() : null;
        if (updateManager && managerAsset is null)
        {
            Alert("This release does not provide a Manager update package.");
            return;
        }
        string components = updateMain && updateManager ? "Launcher and Manager" :
            updateManager ? "Manager" : "Launcher";
        if (MessageBox.Show(this,
            "Update Viora " + components + " to v" + _latestRelease.Version + "?\n\n" +
            "Each download is SHA-256 verified. The updater waits for Viora to close, retains a " +
            ".previous rollback copy of each replaced EXE, then restarts Viora. " +
            "Existing settings and statistics are preserved. Staged copies are deleted after success.\n\n" +
            (ReleaseUpdateService.HasDesktopRuntime8
                ? "Smaller .NET Desktop Runtime 8 files will be preferred if available in this release."
                : "Full self-contained files will be downloaded because the required desktop runtime was not detected."),
            "Viora verified update", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _downloadingUpdate = true;
        UpdatesDownload.IsEnabled = false;
        UpdatesResult.Text = "Preparing verified update…";
        bool installing = false;
        try
        {
            VerifiedUpdate? main = null, manager = null;
            if (updateMain)
            {
                var asset = _latestRelease.SelectLauncher();
                var progress = new Progress<int>(p =>
                    UpdatesResult.Text = "Downloading Launcher (" + asset.FileName + ")… " + p + "%");
                main = await ReleaseUpdateService.DownloadVerifiedAsync(asset, progress);
            }
            if (updateManager)
            {
                var asset = managerAsset!;
                var progress = new Progress<int>(p =>
                    UpdatesResult.Text = "Downloading Manager (" + asset.FileName + ")… " + p + "%");
                manager = await ReleaseUpdateService.DownloadVerifiedAsync(asset, progress);
            }
            UpdatesResult.Text = "Checksums verified. Restarting to apply update…";
            Storage.Save(_state);
            UpdateInstallerService.Start(main, manager);
            installing = true;
            Close();
        }
        catch (Exception ex)
        {
            UpdatesResult.Text = "Could not start update: " + ex.Message +
                "\nCurrent EXEs were not intentionally changed. Staged downloads may be reused.";
            Alert(UpdatesResult.Text);
        }
        finally
        {
            _downloadingUpdate = false;
            if (!installing) RefreshUpdateOffer();
        }
    }

    private void RecoveryExport_Click(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog { Filter = "Viora backup (*.zip)|*.zip", AddExtension = true,
            FileName = "Viora-backup-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".zip" };
        if (picker.ShowDialog() != true) return;
        try { Storage.Save(_state); int count = RecoveryService.Export(picker.FileName); RecoveryStatus.Text = $"Backed up {count} files to {picker.FileName}"; }
        catch (Exception ex) { Alert("Backup failed: " + ex.Message); }
    }
    private void RecoveryRestore_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Viora backup (*.zip)|*.zip" };
        if (picker.ShowDialog() != true) return;
        if (MessageBox.Show("Stage this backup for restoration at next startup?\n\nSaved account/settings files will be overwritten and local sign-in may change. A safety copy will be retained in backups.",
            "Restore Viora backup", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            int count = RecoveryService.StageRestore(picker.FileName);
            RecoveryStatus.Text = $"{count} files staged. Exit Viora completely, then start it again to apply.";
            SetStatus("Recovery staged. Restart Viora to apply; use tray Exit if minimised.");
        }
        catch (Exception ex) { Alert("Restore failed: " + ex.Message); }
    }
    private void RecoveryOpen_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Storage.BackupsDir);
        Process.Start(new ProcessStartInfo(Storage.BackupsDir) { UseShellExecute = true });
    }
    private void TraySetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingV08 || !_ready) return;
        _state.MinimizeToTray = TrayEnabled.IsChecked == true;
        Persist();
    }
    private void InitializeTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Viora", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("Launch Roblox", null, (_, _) => Dispatcher.Invoke(() =>
        {
            if (_isSignedIn) Launch_Click(this, new RoutedEventArgs());
            else ShowFromTray();
        }));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit Viora", null, (_, _) => Dispatcher.Invoke(Close));
        System.Drawing.Icon icon;
        try { icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "") ?? System.Drawing.SystemIcons.Application; }
        catch { icon = System.Drawing.SystemIcons.Application; }
        _tray = new System.Windows.Forms.NotifyIcon { Icon = icon, Text = "Viora", Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }
    private void ShowFromTray()
    {
        Show(); WindowState = WindowState.Normal; ShowInTaskbar = true; Activate();
    }
    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (_state?.MinimizeToTray == true && WindowState == WindowState.Minimized)
        {
            Hide(); ShowInTaskbar = false;
        }
    }
    private void DisposeTray()
    {
        if (_tray is null) return;
        _tray.Visible = false;
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose(); _tray = null;
    }
}
