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

    // A "workspace" is simply a named group of existing Quick Launcher games.
    // The saved Folder property remains the only source of truth: users upgrading
    // from v1.3 keep every game and assigned performance profile unchanged.
    private const string AllQuickWorkspaces = "All workspaces";
    private bool _updatingQuickWorkspaceChoices;

    private static string WorkspaceName(LaunchShortcut shortcut) =>
        string.IsNullOrWhiteSpace(shortcut.Folder) ? "Games" : shortcut.Folder.Trim();

    private void RefreshQuickList(string? preferredWorkspace = null)
    {
        if (QuickList is null || QuickWorkspaceFilter is null) return;
        string selectedId = (QuickList.SelectedItem as LaunchShortcut)?.Id ?? "";
        string previous = preferredWorkspace ??
            (QuickWorkspaceFilter.SelectedItem as string ?? AllQuickWorkspaces);
        var folders = _state.LaunchShortcuts.Select(WorkspaceName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

        string chosen = folders.FirstOrDefault(folder =>
            folder.Equals(previous, StringComparison.OrdinalIgnoreCase)) ?? AllQuickWorkspaces;
        var choices = new[] { AllQuickWorkspaces }.Concat(folders).ToList();
        // Suppress SelectionChanged callbacks while replacing ComboBox items.
        if (!QuickWorkspaceFilter.Items.OfType<string>().SequenceEqual(choices, StringComparer.Ordinal) ||
            !string.Equals(QuickWorkspaceFilter.SelectedItem as string, chosen, StringComparison.Ordinal))
        {
            _updatingQuickWorkspaceChoices = true;
            try
            {
                QuickWorkspaceFilter.ItemsSource = choices;
                QuickWorkspaceFilter.SelectedItem = chosen;
            }
            finally { _updatingQuickWorkspaceChoices = false; }
        }

        string filter = QuickSearch?.Text?.Trim() ?? "";
        var matched = _state.LaunchShortcuts
            .Where(q => (chosen == AllQuickWorkspaces ||
                         WorkspaceName(q).Equals(chosen, StringComparison.OrdinalIgnoreCase)) &&
                        (q.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                         WorkspaceName(q).Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                         q.PlaceId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                           .Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(q => WorkspaceName(q), StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(q => q.LastLaunchedUtc)
            .ThenBy(q => q.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        QuickList.ItemsSource = matched;
        if (!string.IsNullOrEmpty(selectedId))
            QuickList.SelectedItem = matched.FirstOrDefault(q => q.Id == selectedId);
        QuickWorkspaceCount.Text = $"{matched.Count} shown · {folders.Count} workspaces";
        QuickWorkspaceRename.IsEnabled = chosen != AllQuickWorkspaces;
    }

    private void QuickWorkspaceFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && !_updatingQuickWorkspaceChoices) RefreshQuickList();
    }

    private string? PromptWorkspaceRename(string oldName)
    {
        var dialog = new Window
        {
            Title = "Rename game workspace", Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 395, Height = 188, ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)Application.Current.Resources["Panel"]
        };
        var layout = new StackPanel { Margin = new Thickness(18) };
        layout.Children.Add(new TextBlock
        {
            Text = "Name for \"" + oldName + "\"", Margin = new Thickness(0, 0, 0, 10)
        });
        var name = new TextBox { Text = oldName, MaxLength = 40, Height = 32 };
        layout.Children.Add(name);
        var save = new Button
        {
            Content = "Rename", HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0), Width = 90
        };
        save.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(name.Text)) dialog.DialogResult = true;
        };
        layout.Children.Add(save);
        dialog.Content = layout;
        dialog.Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
        return dialog.ShowDialog() == true ? name.Text.Trim() : null;
    }

    private void QuickWorkspaceRename_Click(object sender, RoutedEventArgs e)
    {
        if (QuickWorkspaceFilter.SelectedItem is not string previous ||
            previous == AllQuickWorkspaces) return;
        var updated = PromptWorkspaceRename(previous);
        if (updated is null || string.Equals(previous, updated, StringComparison.Ordinal)) return;
        if (updated.Equals(AllQuickWorkspaces, StringComparison.OrdinalIgnoreCase))
        {
            Alert("'All workspaces' is reserved for the workspace filter.");
            return;
        }
        if (updated.Length == 0 || updated.Length > 40)
        {
            Alert("Workspace names must contain 1 to 40 characters.");
            return;
        }
        if (_state.LaunchShortcuts.Any(q =>
            !WorkspaceName(q).Equals(previous, StringComparison.OrdinalIgnoreCase) &&
            WorkspaceName(q).Equals(updated, StringComparison.OrdinalIgnoreCase)))
        {
            if (MessageBox.Show(this,
                "This workspace already exists. Combine the two groups?",
                "Merge workspaces", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        }
        foreach (var item in _state.LaunchShortcuts.Where(q =>
            WorkspaceName(q).Equals(previous, StringComparison.OrdinalIgnoreCase)))
            item.Folder = updated;
        Persist();
        RefreshQuickList(updated);
        SetStatus("Renamed game workspace to \"" + updated + "\". Shortcuts and profiles were preserved.");
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
            var folder = string.IsNullOrWhiteSpace(QuickFolder.Text) ? "Games" : QuickFolder.Text.Trim();
            if (folder.Equals(AllQuickWorkspaces, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("'All workspaces' is reserved for the workspace filter.");
            var entry = new LaunchShortcut { PlaceId = id, Name = QuickName.Text.Trim(),
                Folder = folder,
                ProfileId = (QuickProfile.SelectedItem as UserProfile)?.Id ?? "",
                Fps = SelectedFps(), Quality = SelectedQuality() };
            _state.LaunchShortcuts.Add(entry);
            Persist(); RefreshQuickList(entry.Folder); QuickList.SelectedItem = entry;
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
            var folder = string.IsNullOrWhiteSpace(QuickFolder.Text) ? "Games" : QuickFolder.Text.Trim();
            if (folder.Equals(AllQuickWorkspaces, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("'All workspaces' is reserved for the workspace filter.");
            entry.Folder = folder;
            entry.ProfileId = (QuickProfile.SelectedItem as UserProfile)?.Id ?? "";
            entry.Fps = SelectedFps(); entry.Quality = SelectedQuality();
            Persist(); RefreshQuickList(entry.Folder); QuickList.SelectedItem = entry;
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
                // Dispose process handles promptly; repeated quick-launch attempts must not leak handles.
                var clients = Process.GetProcessesByName("RobloxPlayerBeta");
                try
                {
                    if (clients.Any(p => { try { return !p.HasExited; } catch (InvalidOperationException) { return false; } }))
                        throw new InvalidOperationException("Close Roblox before opening a game with a performance profile.");
                }
                finally { foreach (var client in clients) client.Dispose(); }
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
