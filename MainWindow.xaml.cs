using System.IO;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace Viora;

public partial class MainWindow : Window
{
    private readonly AppState _state;
    private readonly ObservableCollection<FlagDefinition> _catalog;
    private bool _ready;
    private bool _settingsReadOnly;
    private bool _readOnlyWarningShown;
    private bool _updatingModSelection;
    private Brush _selectedNavBrush = new SolidColorBrush(Color.FromRgb(82, 49, 128));
    private Brush _idleNavBrush = new SolidColorBrush(Color.FromRgb(33, 24, 47));
    private string _currentPage = "";

    private UserProfile ActiveProfile => _state.Profiles.First(p => p.Id == _state.ActiveProfileId);
    private RobloxInstallation? SelectedInstallation => InstallationCombo.SelectedItem as RobloxInstallation;

    public MainWindow()
    {
        InitializeComponent();
        try { RecoveryService.ApplyPendingRestore(); }
        catch (Exception ex) { MessageBox.Show("A staged backup could not be restored. Original settings were left for inspection.\n\n" + ex.Message, "Viora recovery", MessageBoxButton.OK, MessageBoxImage.Warning); }
        try { _state = Storage.Load(); }
        catch (Exception ex)
        {
            // Keep the original unreadable settings file intact for recovery.
            _settingsReadOnly = true;
            MessageBox.Show("Settings could not be loaded. Existing files were not modified.\n\n" + ex.Message,
                "Viora settings", MessageBoxButton.OK, MessageBoxImage.Error);
            _state = new AppState();
            var initial = new UserProfile();
            _state.Profiles.Add(initial);
            _state.ActiveProfileId = initial.Id;
        }
        _catalog = new ObservableCollection<FlagDefinition>(Storage.LoadCatalog());
        InitializeV05();
        FlagsGrid.ItemsSource = _catalog;
        var view = CollectionViewSource.GetDefaultView(FlagsGrid.ItemsSource);
        view.Filter = FilterFlag;
        RefreshCategories();
        RefreshInstallations();
        RefreshProfiles();
        RefreshMods();
        CustomIconText.Text = string.IsNullOrWhiteSpace(_state.CustomIconFile) ? "No custom icon selected" : _state.CustomIconFile;
        _ready = true;
        SwitchPage("dashboard");
        RefreshDashboard();
        RefreshPerformanceSummary();
        RefreshPerformancePreview();
        SetStatus("Ready. The built-in FastFlag allowlist is a September 2025 snapshot.");
        InitializePersonalFeatures();
        InitializeGameHub();
        InitializeV08();
        InitializePolish();
    }

    private void SetStatus(string message) => StatusText.Text = message;
    private void Persist()
    {
        if (_settingsReadOnly)
        {
            if (!_readOnlyWarningShown)
            {
                _readOnlyWarningShown = true;
                MessageBox.Show(this,
                    "Your existing settings couldn't be loaded. Changes in this session cannot be saved. " +
                    "The original settings file has not been overwritten; restore a backup or repair the file.",
                    "Viora settings are read-only", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            SetStatus("Read-only mode: original settings preserved.");
            return;
        }
        Storage.Save(_state);
        RefreshDashboard();
        // Avoid heavy offscreen layout and recursive binding callbacks when
        // changing settings in an unrelated page. The destination pages
        // refresh on navigation.
        if (_currentPage == "performance") RefreshPerformanceSummary();
        if (_currentPage == "stats") RefreshStatsUI();
        if (_currentPage is "gamehub" or "dashboard") RefreshGameHub(force: true);
        if (_currentPage == "flags") ReloadFilter();
    }
    private void RefreshInstallations()
    {
        var current = SelectedInstallation?.DirectoryPath;
        var items = RobloxService.FindInstallations();
        InstallationCombo.ItemsSource = items;
        InstallationCombo.SelectedItem = items.FirstOrDefault(x => x.DirectoryPath == current) ?? items.FirstOrDefault();
        RefreshDashboard();
    }
    private void RefreshDashboard()
    {
        if (DashClientStatus is null) return;
        var installation = SelectedInstallation;
        DashClientStatus.Text = installation is null ? "Not detected" : "Installed";
        DashClientVersion.Text = installation?.Version ?? "Install Roblox from its official website";
        if (_state?.Profiles is null || _state.Profiles.Count == 0) return;
        DashProfileName.Text = ActiveProfile.Name;
        DashFlagCount.Text = ActiveProfile.Flags.Count.ToString(CultureInfo.InvariantCulture);
    }
    private void SwitchPage(string page)
    {
        var changedPage = _currentPage != page;
        _currentPage = page;
        LauncherPage.Visibility = page == "launcher" ? Visibility.Visible : Visibility.Collapsed;
        GameHubPage.Visibility = page == "gamehub" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePage.Visibility = page == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        SplashPage.Visibility = page == "splash" ? Visibility.Visible : Visibility.Collapsed;
        DashboardPage.Visibility = page == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        StatsPage.Visibility = page == "stats" ? Visibility.Visible : Visibility.Collapsed;
        DiscordPage.Visibility = page == "discord" ? Visibility.Visible : Visibility.Collapsed;
        PerformancePage.Visibility = page == "performance" ? Visibility.Visible : Visibility.Collapsed;
        FlagsPage.Visibility = page == "flags" ? Visibility.Visible : Visibility.Collapsed;
        ProfilesPage.Visibility = page == "profiles" ? Visibility.Visible : Visibility.Collapsed;
        ModsPage.Visibility = page == "mods" ? Visibility.Visible : Visibility.Collapsed;
        ToolsPage.Visibility = page == "tools" ? Visibility.Visible : Visibility.Collapsed;
        NavDashboard.Background = page == "dashboard" ? _selectedNavBrush : _idleNavBrush;
        NavFlags.Background = page == "flags" ? _selectedNavBrush : _idleNavBrush;
        NavPerformance.Background = page == "performance" ? _selectedNavBrush : _idleNavBrush;
        NavProfiles.Background = page == "profiles" ? _selectedNavBrush : _idleNavBrush;
        NavLauncher.Background = page == "launcher" ? _selectedNavBrush : _idleNavBrush;
        NavMods.Background = page == "mods" ? _selectedNavBrush : _idleNavBrush;
        NavTools.Background = page == "tools" ? _selectedNavBrush : _idleNavBrush;
        NavAppearance.Background = page == "appearance" ? _selectedNavBrush : _idleNavBrush;
        NavSplash.Background = page == "splash" ? _selectedNavBrush : _idleNavBrush;
        NavStats.Background = page == "stats" ? _selectedNavBrush : _idleNavBrush;
        NavGameHub.Background = page == "gamehub" ? _selectedNavBrush : _idleNavBrush;
        NavDiscord.Background = page == "discord" ? _selectedNavBrush : _idleNavBrush;
        NavGroupOptimize.Background = page is "performance" or "flags" ? _selectedNavBrush : _idleNavBrush;
        NavGroupWorkspace.Background = page is "profiles" or "mods" or "launcher" ? _selectedNavBrush : _idleNavBrush;
        NavGroupActivity.Background = page is "stats" or "discord" or "gamehub" ? _selectedNavBrush : _idleNavBrush;
        NavGroupPersonalize.Background = page is "appearance" or "tools" or "splash" ? _selectedNavBrush : _idleNavBrush;
        if (changedPage)
        {
            var group = page switch
            {
                "performance" or "flags" => "optimize",
                "profiles" or "mods" or "launcher" => "workspace",
                "stats" or "discord" or "gamehub" => "activity",
                "appearance" or "tools" or "splash" => "personalize",
                _ => ""
            };
            ExpandNavigation(group);
        }
        PageHeading.Text = page switch
        {
            "flags" => "FastFlag Explorer", "performance" => "Performance", "profiles" => "Profiles", "mods" => "Asset Studio",
            "launcher" => "Quick Launcher", "tools" => "Utilities", "stats" => "My statistics", "gamehub" => "Game Activity Hub", "discord" => "Discord Presence", "appearance" => "Appearance", "splash" => "Splash Screen", _ => "Overview"
        };
        if (changedPage && _ready && page == "performance")
        {
            RefreshRobloxGraphics();
            RefreshPerformanceSummary();
        }
        if (changedPage && _ready && page == "flags") ReloadFilter();
        if (changedPage && _ready && page == "gamehub") RefreshGameHub(force: true);
        if (changedPage && _ready && page == "stats") RefreshStatsUI();
        if (changedPage && _ready && page == "dashboard") RefreshGameHub(force: true);
        if (changedPage && _ready) AnimatePage(page);
    }
    private string _openNavigationGroup = "";
    private void ExpandNavigation(string group)
    {
        _openNavigationGroup = group;
        GroupOptimizeChildren.Visibility = group == "optimize" ? Visibility.Visible : Visibility.Collapsed;
        GroupWorkspaceChildren.Visibility = group == "workspace" ? Visibility.Visible : Visibility.Collapsed;
        GroupActivityChildren.Visibility = group == "activity" ? Visibility.Visible : Visibility.Collapsed;
        GroupPersonalizeChildren.Visibility = group == "personalize" ? Visibility.Visible : Visibility.Collapsed;
        NavOptimizeArrow.Text = group == "optimize" ? "⌃" : "⌄";
        NavWorkspaceArrow.Text = group == "workspace" ? "⌃" : "⌄";
        NavActivityArrow.Text = group == "activity" ? "⌃" : "⌄";
        NavPersonalizeArrow.Text = group == "personalize" ? "⌃" : "⌄";
    }
    private void NavGroupOptimize_Click(object sender, RoutedEventArgs e) => ExpandNavigation(_openNavigationGroup == "optimize" ? "" : "optimize");
    private void NavGroupWorkspace_Click(object sender, RoutedEventArgs e) => ExpandNavigation(_openNavigationGroup == "workspace" ? "" : "workspace");
    private void NavGroupActivity_Click(object sender, RoutedEventArgs e) => ExpandNavigation(_openNavigationGroup == "activity" ? "" : "activity");
    private void NavGroupPersonalize_Click(object sender, RoutedEventArgs e) => ExpandNavigation(_openNavigationGroup == "personalize" ? "" : "personalize");

    private void NavDashboard_Click(object sender, RoutedEventArgs e) => SwitchPage("dashboard");
    private void NavFlags_Click(object sender, RoutedEventArgs e) => SwitchPage("flags");
    private void NavPerformance_Click(object sender, RoutedEventArgs e) => SwitchPage("performance");
    private void GoPerformance_Click(object sender, RoutedEventArgs e) => SwitchPage("performance");
    private void NavProfiles_Click(object sender, RoutedEventArgs e) => SwitchPage("profiles");
    private void NavLauncher_Click(object sender, RoutedEventArgs e) => SwitchPage("launcher");
    private void NavMods_Click(object sender, RoutedEventArgs e) => SwitchPage("mods");
    private void NavTools_Click(object sender, RoutedEventArgs e) => SwitchPage("tools");
    private void NavAppearance_Click(object sender, RoutedEventArgs e) => SwitchPage("appearance");
    private void NavStats_Click(object sender, RoutedEventArgs e) => SwitchPage("stats");
    private void NavGameHub_Click(object sender, RoutedEventArgs e) => SwitchPage("gamehub");
    private void NavDiscord_Click(object sender, RoutedEventArgs e) => SwitchPage("discord");
    private void GoStats_Click(object sender, RoutedEventArgs e) => SwitchPage("stats");
    private void GoFlags_Click(object sender, RoutedEventArgs e) => SwitchPage("flags");
    private void GoProfiles_Click(object sender, RoutedEventArgs e) => SwitchPage("profiles");
    private void InstallationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_ready) RefreshDashboard(); }

    // Performance Center - quick profiles are not an FPS unlocker.
    private string SelectedPerformanceLevel => (PerformancePresetCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? PerformancePresets.Max;

    private void RefreshPerformanceSummary()
    {
        if (PerformanceActiveText is null || _state?.Profiles is null || _state.Profiles.Count == 0) return;
        PerformanceActiveText.Text = "Active profile: " + ActiveProfile.Name + "  •  Performance: " + ActiveProfile.PerformancePreset +
            "  •  Presets apply directly when Roblox is closed.";
    }

    private void RefreshPerformancePreview()
    {
        if (PerformancePresetPreview is null || PerformancePresetCombo is null || PerfGrassToggle is null) return;
        var level = SelectedPerformanceLevel;
        PerfGrassToggle.IsEnabled = level != PerformancePresets.Default;
        var grass = PerfGrassToggle.IsChecked == true && level != PerformancePresets.Default;
        var desired = PerformancePresets.Preview(level, grass);
        if (desired.Count == 0)
        {
            PerformancePresetPreview.Text = "Restores Roblox-managed defaults for texture quality, antialiasing and grass. Does not change any other FastFlags in the active profile.";
            return;
        }
        var summary = level == PerformancePresets.Max
            ? "Requests lower texture detail (internal level 0) and minimal forced MSAA (1 sample)."
            : "Requests moderate texture detail (internal level 1) and 2x forced MSAA.";
        PerformancePresetPreview.Text = summary + (grass ? " Also requests reduced grass distance." : "") +
            " These values are experimental and may be ignored or behave differently after an update. Changes: " +
            string.Join(", ", desired.Select(x => x.Key + "=" + x.Value)) + ".";
    }

    private void PerformancePresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => RefreshPerformancePreview();

    private void PerfGrassToggle_Changed(object sender, RoutedEventArgs e)
        => RefreshPerformancePreview();

    private void ApplyPerformancePreset_Click(object sender, RoutedEventArgs e)
    {
        var level = SelectedPerformanceLevel;
        var grass = PerfGrassToggle.IsChecked == true && level != PerformancePresets.Default;
        var preview = PerformancePresets.Preview(level, grass);
        var summary = preview.Count == 0 ? "Remove Viora-managed performance overrides?" :
            "Save and apply " + preview.Count + " experimental rendering overrides?";
        if (MessageBox.Show(summary + "\n\nOther FastFlags will be preserved. If Roblox is closed and an installation is selected, Viora will apply the updated profile now.",
            "Confirm performance preset", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var published = _catalog.Where(x => x.IsAllowlisted).Select(x => x.Name).ToList();
            PerformancePresets.Apply(ActiveProfile, level, grass, published);
            Persist();
            if (SelectedInstallation is not null && ApplyCurrentProfile())
                SetStatus("Performance preset saved. Changes apply during the next Roblox session.");
            else
                SetStatus("Performance preset saved in the current profile. Select an installation and apply the profile when Roblox is closed.");
        }
        catch (Exception ex) { Alert("Performance preset failed: " + ex.Message); }
    }

    private void ClearPerformanceOverrides_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Clear performance-related overrides from the active profile? Other FastFlags will stay saved. Apply the profile afterwards to restore the client entries.",
            "Restore performance defaults", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        PerformancePresets.Clear(ActiveProfile);
        Persist();
        if (SelectedInstallation is not null && ApplyCurrentProfile())
            SetStatus("Performance FastFlag overrides cleared and the profile synced to Roblox.");
        else SetStatus("Performance overrides cleared in the profile; sync them when Roblox is closed.");
    }

    private static void OpenSystemSettings(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { Alert("Couldn't open Windows Settings: " + ex.Message); }
    }
    private void OpenGameMode_Click(object sender, RoutedEventArgs e) => OpenSystemSettings("ms-settings:gaming-gamemode");
    private void OpenWindowsGraphics_Click(object sender, RoutedEventArgs e) => OpenSystemSettings("ms-settings:display-advancedgraphics");
    private void OpenWindowsDisplay_Click(object sender, RoutedEventArgs e) => OpenSystemSettings("ms-settings:display-advanced");
    private void OpenFpsGuide_Click(object sender, RoutedEventArgs e)
        => RobloxService.OpenUrl("https://devforum.roblox.com/t/introducing-the-maximum-framerate-setting/2995965");

    // FastFlag catalogue and editor
    private bool FilterFlag(object o)
    {
        if (o is not FlagDefinition f) return false;
        if (SupportedOnly?.IsChecked == true && !f.IsAllowlisted) return false;
        if (FlagConfiguredOnly?.IsChecked == true && !ActiveProfile.Flags.ContainsKey(f.Name)) return false;
        var category = FlagCategoryFilter?.SelectedItem?.ToString() ?? "All categories";
        if (category != "All categories" && f.Category != category) return false;
        var q = FlagSearchBox?.Text?.Trim() ?? "";
        return q.Length == 0 || f.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || f.Category.Contains(q, StringComparison.OrdinalIgnoreCase)
            || f.Description.Contains(q, StringComparison.OrdinalIgnoreCase);
    }
    private void RefreshCategories()
    {
        var previous = FlagCategoryFilter.SelectedItem?.ToString() ?? "All categories";
        var cats = new[] { "All categories" }.Concat(_catalog.Select(x => x.Category).Distinct().OrderBy(x => x)).ToList();
        FlagCategoryFilter.ItemsSource = cats;
        FlagCategoryFilter.SelectedItem = cats.Contains(previous) ? previous : "All categories";
    }
    private void ReloadFilter()
    {
        if (!_ready) return;
        var view = CollectionViewSource.GetDefaultView(FlagsGrid.ItemsSource);
        view?.Refresh();
        if (FlagResultCount is not null) FlagResultCount.Text = $"{view?.Cast<object>().Count() ?? 0:N0} matching entries · {ActiveProfile.Flags.Count} configured";
    }
    private void FlagFilter_Changed(object sender, TextChangedEventArgs e) => ReloadFilter();
    private void FlagCategoryFilter_Changed(object sender, SelectionChangedEventArgs e) => ReloadFilter();
    private void FlagSupportedOnly_Changed(object sender, RoutedEventArgs e) => ReloadFilter();
    private void FlagConfiguredOnly_Changed(object sender, RoutedEventArgs e) => ReloadFilter();
    private void ClearFlagSearch_Click(object sender, RoutedEventArgs e)
    {
        FlagSearchBox.Clear();
        FlagCategoryFilter.SelectedIndex = 0;
        FlagConfiguredOnly.IsChecked = false;
        ReloadFilter();
    }
    private void FlagsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || FlagsGrid.SelectedItem is not FlagDefinition flag) return;
        FlagDetailName.Text = flag.Name;
        FlagDetailStatus.Text = flag.Status + " · " + flag.Category + " · " + flag.Type;
        FlagDetailDescription.Text = flag.Description;
        FlagDetailWarning.Text = string.IsNullOrWhiteSpace(flag.Warning) ? "No verified warning available. Treat undocumented entries as experimental." : flag.Warning;
        FlagDetailConfidence.Text = flag.Confidence;
        FlagExampleValue.Text = string.IsNullOrWhiteSpace(flag.ExampleValue) ? "No documented example value available." : "Example input: " + flag.ExampleValue;
        FlagValueBox.Text = ActiveProfile.Flags.TryGetValue(flag.Name, out var configured) ? configured : flag.ExampleValue;
        FlagBooleanValue.Visibility = flag.Type == "Boolean" ? Visibility.Visible : Visibility.Collapsed;
        FlagValueBox.Visibility = flag.Type == "Boolean" ? Visibility.Collapsed : Visibility.Visible;
        if (flag.Type == "Boolean")
        {
            var value = ActiveProfile.Flags.TryGetValue(flag.Name, out var current) ? current : flag.ExampleValue;
            FlagBooleanValue.SelectedIndex = bool.TryParse(value, out var booleanValue) ? (booleanValue ? 0 : 1) : -1;
        }
        FlagBooleanValue.IsEnabled = flag.IsAllowlisted;
        FlagValueBox.IsEnabled = flag.IsAllowlisted;
        SaveFlagButton.IsEnabled = flag.IsAllowlisted;
        RemoveFlagButton.IsEnabled = flag.IsAllowlisted;
    }
    private void SaveFlag_Click(object sender, RoutedEventArgs e)
    {
        if (FlagsGrid.SelectedItem is not FlagDefinition flag || !flag.IsAllowlisted) return;
        if (flag.Type == "Boolean" && FlagBooleanValue.SelectedItem is null)
        {
            Alert("Choose On / True or Off / False before saving.");
            return;
        }
        var value = flag.Type == "Boolean"
            ? ((FlagBooleanValue.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "")
            : FlagValueBox.Text.Trim();
        if (flag.Type == "Boolean")
        {
            if (!bool.TryParse(value, out var parsed)) { Alert("Enter True or False for this flag."); return; }
            value = parsed ? "True" : "False";
        }
        else if (flag.Type == "Integer")
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) { Alert("Enter an integer for this flag."); return; }
            value = parsed.ToString(CultureInfo.InvariantCulture);
        }
        else if (string.IsNullOrWhiteSpace(value)) { Alert("Enter a value first."); return; }
        if (flag.Category == "Graphics API" && value == "True")
        {
            var conflicts = ActiveProfile.Flags.Where(kv => kv.Key != flag.Name && kv.Key.StartsWith("FFlagDebugGraphicsPrefer", StringComparison.Ordinal) && kv.Value == "True").ToList();
            if (conflicts.Count > 0 && MessageBox.Show("Another graphics backend is already preferred. Save this setting anyway?", "Graphics backend conflict", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        }
        ActiveProfile.Flags[flag.Name] = value;
        if (PerformancePresets.IsManagedFlag(flag.Name)) ActiveProfile.PerformancePreset = "Custom";
        Persist();
        ReloadFilter();
        SetStatus("Saved " + flag.Name + " = " + value + " in '" + ActiveProfile.Name + "'. Click Apply to write to Roblox.");
    }
    private void RemoveFlag_Click(object sender, RoutedEventArgs e)
    {
        if (FlagsGrid.SelectedItem is not FlagDefinition flag || !flag.IsAllowlisted) return;
        if (ActiveProfile.Flags.Remove(flag.Name)) { if (PerformancePresets.IsManagedFlag(flag.Name)) ActiveProfile.PerformancePreset = "Custom"; Persist(); SetStatus("Removed " + flag.Name + " from the profile. Apply it to sync."); }
        FlagValueBox.Text = flag.ExampleValue;
        ReloadFilter();
        if (flag.Type == "Boolean") FlagBooleanValue.SelectedIndex = 0;
    }
    private void ImportFlags_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "JSON files (*.json)|*.json", Title = "Import ClientAppSettings-style JSON" };
        if (picker.ShowDialog() != true) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(picker.FileName));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected a JSON object of flag names and values.");
            var allowed = _catalog.Where(x => x.IsAllowlisted).ToDictionary(x => x.Name, StringComparer.Ordinal);
            var imported = 0; var skipped = 0;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!allowed.TryGetValue(prop.Name, out var definition)) { skipped++; continue; }
                var value = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : prop.Value.ToString();
                if (definition.Type == "Boolean")
                {
                    if (!bool.TryParse(value, out var b)) { skipped++; continue; }
                    value = b ? "True" : "False";
                }
                else if (definition.Type == "Integer")
                {
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) { skipped++; continue; }
                    value = n.ToString(CultureInfo.InvariantCulture);
                }
                ActiveProfile.Flags[prop.Name] = value;
                imported++;
            }
            Persist();
            FlagsGrid_SelectionChanged(sender, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, new List<object>(), new List<object>()));
            MessageBox.Show($"Imported {imported} published-allowlist entries into the active profile.\nSkipped {skipped} unlisted or invalid entries.\n\nNo changes were written to Roblox yet.", "FastFlag import");
        }
        catch (Exception ex) { Alert("Import failed: " + ex.Message); }
    }
    private void ExportFlags_Click(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog { Filter = "JSON files (*.json)|*.json", FileName = "ClientAppSettings.json", Title = "Export active profile flags" };
        if (picker.ShowDialog() != true) return;
        try { Storage.WriteJson(picker.FileName, ActiveProfile.Flags); SetStatus("Exported active profile FastFlags."); }
        catch (Exception ex) { Alert("Export failed: " + ex.Message); }
    }
    private async void SyncPublicCatalog_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Fetching public client-setting names from the FFlag Tracker...");
        try
        {
            var entries = await RobloxService.DownloadPublicFlagNamesAsync();
            var known = new HashSet<string>(_catalog.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            var extras = _catalog.Where(x => !x.IsAllowlisted).ToList();
            var added = 0;
            // Detach before bulk add to avoid thousands of DataGrid refreshes.
            FlagsGrid.ItemsSource = null;
            foreach (var f in entries)
            {
                if (!known.Add(f.Name)) continue;
                _catalog.Add(f);
                extras.Add(f);
                added++;
            }
            FlagsGrid.ItemsSource = _catalog;
            CollectionViewSource.GetDefaultView(FlagsGrid.ItemsSource).Filter = FilterFlag;
            Storage.WriteJson(Storage.ExtraCatalogPath, extras);
            SupportedOnly.IsChecked = false;
            RefreshCategories(); ReloadFilter();
            SetStatus($"Synced public flag reference: {entries.Count} tracked names, {added} newly added. Only the 2025 allowlist entries can be configured.");
        }
        catch (Exception ex)
        {
            if (FlagsGrid.ItemsSource is null)
            {
                FlagsGrid.ItemsSource = _catalog;
                CollectionViewSource.GetDefaultView(FlagsGrid.ItemsSource).Filter = FilterFlag;
            }
            Alert("Public flag sync failed: " + ex.Message);
        }
    }
    private void ImportCatalog_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "JSON files (*.json)|*.json", Title = "Import optional reference flag names / descriptions" };
        if (picker.ShowDialog() != true) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(picker.FileName));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Expected a JSON array of names or objects with Name, Description, Category and Type.");
            var added = 0;
            var extras = _catalog.Where(x => !x.IsAllowlisted).ToList();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                FlagDefinition? f = item.ValueKind switch
                {
                    JsonValueKind.String => new FlagDefinition { Name = item.GetString() ?? "" },
                    JsonValueKind.Object => JsonSerializer.Deserialize<FlagDefinition>(item.GetRawText(), Storage.JsonOptions),
                    _ => null
                };
                if (f == null || string.IsNullOrWhiteSpace(f.Name) || f.Name.Length > 160 || _catalog.Any(x => x.Name.Equals(f.Name, StringComparison.OrdinalIgnoreCase))) continue;
                f.IsAllowlisted = false;
                f.Confidence = "Imported reference; not independently verified";
                f.Description = string.IsNullOrWhiteSpace(f.Description) ? "No verified public description is available." : f.Description;
                f.Category = string.IsNullOrWhiteSpace(f.Category) ? "Reference" : f.Category;
                f.Type = string.IsNullOrWhiteSpace(f.Type) ? "Unknown" : f.Type;
                _catalog.Add(f); extras.Add(f); added++;
            }
            Storage.WriteJson(Storage.ExtraCatalogPath, extras);
            SupportedOnly.IsChecked = false;
            RefreshCategories(); ReloadFilter();
            MessageBox.Show($"Added {added} reference entries. These entries are browse-only; importing them does not establish official allowlist status.", "Reference catalogue");
        }
        catch (Exception ex) { Alert("Catalogue import failed: " + ex.Message); }
    }
    private void OpenAllowlist_Click(object sender, RoutedEventArgs e) => RobloxService.OpenUrl("https://devforum.roblox.com/t/allowlist-for-local-client-configuration-via-fast-flags/3966569");

    // Profiles
    private void RefreshProfiles()
    {
        if (ProfileList is null) return;
        var selectedId = (ProfileList.SelectedItem as UserProfile)?.Id ?? _state.ActiveProfileId;
        ProfileList.ItemsSource = null;
        ProfileList.ItemsSource = _state.Profiles;
        ProfileList.SelectedItem = _state.Profiles.FirstOrDefault(x => x.Id == selectedId) ?? _state.Profiles.First();
        RefreshProfileDetail();
    }
    private void RefreshProfileDetail()
    {
        if (ProfileDetailTitle is null) return;
        var profile = ProfileList.SelectedItem as UserProfile;
        ProfileDetailTitle.Text = profile is null ? "No profile selected" : profile.Name + (profile.Id == _state.ActiveProfileId ? " · ACTIVE" : "");
        ProfileDetailCount.Text = profile is null ? "" : profile.Flags.Count + " configured flags";
    }
    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_ready) RefreshProfileDetail(); }
    private void CreateProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = ProfileNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { Alert("Type a profile name first."); return; }
        var profile = new UserProfile { Name = name };
        _state.Profiles.Add(profile); Persist(); RefreshProfiles(); ProfileList.SelectedItem = profile;
        ProfileNameBox.Clear(); SetStatus("Created profile '" + name + "'.");
    }
    private void DuplicateProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not UserProfile source) return;
        var copy = new UserProfile { Name = source.Name + " (copy)", PerformancePreset = source.PerformancePreset, Flags = new Dictionary<string, string>(source.Flags, StringComparer.Ordinal) };
        _state.Profiles.Add(copy); Persist(); RefreshProfiles(); ProfileList.SelectedItem = copy;
    }
    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not UserProfile profile) return;
        if (_state.Profiles.Count == 1) { Alert("Keep at least one profile."); return; }
        if (MessageBox.Show("Delete profile '" + profile.Name + "'?", "Confirm deletion", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _state.Profiles.Remove(profile);
        if (_state.ActiveProfileId == profile.Id) _state.ActiveProfileId = _state.Profiles[0].Id;
        Persist(); RefreshProfiles();
    }
    private void ActivateProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not UserProfile profile) return;
        _state.ActiveProfileId = profile.Id;
        Persist(); RefreshProfileDetail();
        if (FlagsGrid.SelectedItem is FlagDefinition) FlagsGrid_SelectionChanged(sender, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, new List<object>(), new List<object>()));
        SetStatus("Active profile set to '" + profile.Name + "'. Apply it to sync Roblox settings.");
    }

    // Launch + official player configuration
    private bool CanEditInstalledClient()
    {
        if (SelectedInstallation is null) { Alert("Roblox Player was not found. Install Roblox first and restart Viora."); return false; }
        if (Process.GetProcessesByName("RobloxPlayerBeta").Length != 0)
        {
            Alert("Close the Roblox Player before applying or restoring settings and assets.");
            return false;
        }
        return true;
    }
    private bool ApplyCurrentProfile()
    {
        if (!CanEditInstalledClient()) return false;
        try
        {
            var allowed = _catalog.Where(x => x.IsAllowlisted).Select(x => x.Name).ToList();
            RobloxService.ApplyFlags(SelectedInstallation!, ActiveProfile, allowed);
            SetStatus("Applied " + ActiveProfile.Flags.Count + " FastFlag overrides to " + SelectedInstallation!.Version + ".");
            return true;
        }
        catch (Exception ex) { Alert("Could not apply FastFlags: " + ex.Message); return false; }
    }
    private void ApplyProfile_Click(object sender, RoutedEventArgs e) => ApplyCurrentProfile();
    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        // Applying is best-effort; never run an unofficial updater or bypass Roblox's own launcher.
        if (SelectedInstallation is not null && !ApplyCurrentProfile()) return;
        try
        {
            RobloxService.OpenRoblox();
            _state.Stats.LaunchRequests++;
            Persist();
            SetStatus("Sent a request to open the official Roblox app or website. Session detection may take a moment.");
        }
        catch (Exception ex) { Alert("Launch failed: " + ex.Message); }
    }

    // Staged content assets
    private void RefreshMods()
    {
        if (ModList is null) return;
        _updatingModSelection = true;
        var selected = (ModList.SelectedItem as ModEntry)?.Id;
        ModList.ItemsSource = null;
        ModList.ItemsSource = _state.Mods;
        ModList.SelectedItem = _state.Mods.FirstOrDefault(x => x.Id == selected) ?? _state.Mods.FirstOrDefault();
        SelectedModEnabled.IsChecked = (ModList.SelectedItem as ModEntry)?.Enabled ?? false;
        _updatingModSelection = false;
    }
    private void BrowseAsset_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Images and sounds|*.png;*.jpg;*.jpeg;*.dds;*.webp;*.ogg;*.wav;*.mp3;*.ttf;*.otf|All files|*.*" };
        if (picker.ShowDialog() == true) AssetSourceBox.Text = picker.FileName;
    }
    private void StageAsset_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var target = ModManager.ValidateTarget(AssetTargetBox.Text);
            if (_state.Mods.Any(x => x.RelativeTarget.Equals(target, StringComparison.OrdinalIgnoreCase))) { Alert("That destination already has a staged asset. Remove it or edit the existing entry first."); return; }
            var mod = ModManager.StageAsset(AssetSourceBox.Text, target);
            _state.Mods.Add(mod); Persist(); RefreshMods(); ModList.SelectedItem = mod;
            SetStatus("Staged asset '" + mod.Label + "'. No Roblox files changed yet.");
        }
        catch (Exception ex) { Alert("Could not stage asset: " + ex.Message); }
    }
    private void ModList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updatingModSelection) return;
        _updatingModSelection = true;
        SelectedModEnabled.IsChecked = (ModList.SelectedItem as ModEntry)?.Enabled ?? false;
        _updatingModSelection = false;
    }
    private void SelectedModEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _updatingModSelection || ModList.SelectedItem is not ModEntry mod) return;
        mod.Enabled = SelectedModEnabled.IsChecked == true;
        Persist();
    }
    private void ApplyAssets_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditInstalledClient()) return;
        if (MessageBox.Show("Apply enabled local resource replacements to this Roblox version? Original files will be backed up.", "Apply assets", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { var count = ModManager.ApplyAssets(SelectedInstallation!, _state.Mods); SetStatus("Applied " + count + " local assets; originals saved under Viora\\backups."); }
        catch (Exception ex) { Alert("Asset application stopped: " + ex.Message + "\nAny earlier replacements are backed up and can be restored."); }
    }
    private void RestoreAssets_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditInstalledClient()) return;
        try { var count = ModManager.RestoreAssets(SelectedInstallation!, _state.Mods); SetStatus("Restored " + count + " original files for this Roblox version."); }
        catch (Exception ex) { Alert("Asset restoration failed: " + ex.Message); }
    }
    private void DeleteMod_Click(object sender, RoutedEventArgs e)
    {
        if (ModList.SelectedItem is not ModEntry mod) return;
        if (MessageBox.Show("Remove this staged asset? If already applied, restore original files first. Backups are not deleted.", "Remove staged asset", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _state.Mods.Remove(mod); Persist(); RefreshMods();
        try { if (File.Exists(mod.SavedFile)) File.Delete(mod.SavedFile); }
        catch { SetStatus("Removed entry; the staged file couldn't be deleted from disk."); }
    }

    // Utilities
    private async void CheckVersion_Click(object sender, RoutedEventArgs e)
    {
        LatestVersionText.Text = "Checking Roblox's official client version endpoint...";
        try
        {
            var published = await RobloxService.CheckLatestVersionAsync();
            LatestVersionText.Text = "Official published version: " + published + "\nInstalled/selected: " + (SelectedInstallation?.Version ?? "None detected") +
                "\nThe official Roblox launcher handles updates.";
        }
        catch (Exception ex) { LatestVersionText.Text = "Unable to check the official version: " + ex.Message; }
    }
    private void OpenClientFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInstallation is null) { Alert("Roblox Player installation not found."); return; }
        OpenFolder(SelectedInstallation.DirectoryPath);
    }
    private void OpenAppData_Click(object sender, RoutedEventArgs e) => OpenFolder(Storage.Root);
    private static void OpenFolder(string directory)
    {
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + directory + "\"") { UseShellExecute = true });
    }
    private void OpenRobloxWebsite_Click(object sender, RoutedEventArgs e) => RobloxService.OpenUrl("https://www.roblox.com/home");
    private void BackupConfig_Click(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog { Filter = "ZIP archives (*.zip)|*.zip", FileName = "viora-backup.zip" };
        if (picker.ShowDialog() != true) return;
        try { ModManager.ExportConfiguration(_state, picker.FileName); SetStatus("Configuration exported. ZIP includes profiles, references and staged assets."); }
        catch (Exception ex) { Alert("Backup export failed: " + ex.Message); }
    }
    private void UndoFlags_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditInstalledClient()) return;
        if (MessageBox.Show("Undo only the FastFlags last written by Viora for this selected version?", "Undo FastFlags", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { RobloxService.UndoFlagChanges(SelectedInstallation!); SetStatus("Undid Viora-managed FastFlag changes in this Roblox version."); }
        catch (Exception ex) { Alert("Undo failed: " + ex.Message); }
    }
    private void ChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Windows icon (*.ico)|*.ico" };
        if (picker.ShowDialog() != true) return;
        try
        {
            var path = Path.Combine(Storage.Root, "custom-launcher.ico");
            Directory.CreateDirectory(Storage.Root);
            File.Copy(picker.FileName, path, true);
            _state.CustomIconFile = path;
            CustomIconText.Text = path; Persist();
        }
        catch (Exception ex) { Alert("Could not save icon: " + ex.Message); }
    }
    private void CreateShortcut_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null || !exe.EndsWith("Viora.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Launch Viora.exe normally (not through a generic host) to create a desktop shortcut.");
            var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host isn't available.");
            dynamic shell = Activator.CreateInstance(shellType)!;
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Viora.lnk");
            dynamic shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = exe;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
            shortcut.Description = "Viora personal Roblox launcher";
            shortcut.IconLocation = File.Exists(_state.CustomIconFile) ? _state.CustomIconFile : exe;
            shortcut.Save();
            SetStatus("Desktop shortcut created: " + path);
        }
        catch (Exception ex) { Alert("Could not create shortcut: " + ex.Message); }
    }
    private static void Alert(string message) => MessageBox.Show(message, "Viora", MessageBoxButton.OK, MessageBoxImage.Information);
}
