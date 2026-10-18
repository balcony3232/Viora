using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Viora;

public partial class MainWindow
{
    // A tracked game segment is a portion of an observed Roblox process session.
    // Detection can start late or fail; we never backfill unobserved playtime.
    private long? _trackedGamePlace;
    private DateTimeOffset _trackedGameStarted;
    private long _trackedGameSeconds;
    private DateTimeOffset _lastHubRefresh = DateTimeOffset.MinValue;
    private bool _gameHubReady;
    private bool _updatingGameGrid;
    private bool _loadingPresenceFields;
    private string _gameHubFilter = "";
    private int _hubSortMode;
    private bool _hubFavouritesOnly;
    private bool _iconRefreshBusy;
    private readonly HashSet<long> _iconAttempts = new();

    // Fetch public icons asynchronously; UI and game tracking stay responsive if offline.
    private async void BeginGameIconRefresh()
    {
        if (!_gameHubReady || _iconRefreshBusy) return;
        var ids = _state.Stats.Games.Where(g => g.PlaceId > 0 && !_iconAttempts.Contains(g.PlaceId))
            .OrderByDescending(g => g.Favorite).ThenByDescending(g => g.LastPlayedUtc)
            .Take(25).Select(g => g.PlaceId).ToArray();
        if (ids.Length == 0) return;
        _iconRefreshBusy = true;
        foreach (var id in ids) _iconAttempts.Add(id);
        try
        {
            await Task.Yield(); // return to the UI pump before disk/network work.
            await RobloxGameIcons.EnsureCachedAsync(ids);
            foreach (var id in ids)
            {
                var game = _state.Stats.Games.FirstOrDefault(g => g.PlaceId == id);
                if (game is not null) game.Icon = RobloxGameIcons.TryLoadCached(id);
            }
            RefreshSelectedGameInfo(); // Selected-game portrait is set directly, unlike bound card images.
        }
        catch (Exception)
        {
            // Game artwork is optional; the placeholder stays visible.
        }
        finally
        {
            _iconRefreshBusy = false;
            // Continue loading remaining games without blocking the UI.
            if (_state.Stats.Games.Any(g => g.PlaceId > 0 && !_iconAttempts.Contains(g.PlaceId)))
                BeginGameIconRefresh();
        }
    }


    private void InitializeGameHub()
    {
        _gameHubReady = true;
        RefreshGameHub(force: true);
    }

    private void ResetGameTracking()
    {
        _trackedGamePlace = null;
        _trackedGameStarted = default;
        _trackedGameSeconds = 0;
    }

    private GameActivityRecord GetOrCreateGame(long placeId)
    {
        var game = _state.Stats.Games.FirstOrDefault(g => g.PlaceId == placeId);
        if (game is not null) return game;
        game = new GameActivityRecord { PlaceId = placeId };
        _state.Stats.Games.Add(game);
        RefreshPresenceGameChoices();
        return game;
    }

    private void UpdateGameName(long placeId, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var game = GetOrCreateGame(placeId);
        if (game.Name == name) return;
        game.Name = name.Trim();
        // Correct any recent segments recorded before the public API returned the title.
        foreach (var session in _state.Stats.GameSessions.Where(s => s.PlaceId == placeId))
            session.Name = game.Name;
        RefreshGameHub(force: true);
    }

    private void TrackGameObservation(DateTimeOffset now, long observedSeconds)
    {
        if (observedSeconds <= 0) return;
        var current = _currentPlaceId;
        if (current != _trackedGamePlace)
        {
            FinalizeGameTracking(now);
            if (current is long id && id > 0)
            {
                _trackedGamePlace = id;
                _trackedGameStarted = now;
                _trackedGameSeconds = 0;
                var game = GetOrCreateGame(id);
                game.Sessions++;
            }
        }
        if (_trackedGamePlace is not long placeId) return;
        var record = GetOrCreateGame(placeId);
        if (_currentGameName is not null && _currentPlaceId == placeId)
            record.Name = _currentGameName;
        record.ObservedSeconds += observedSeconds;
        record.LastPlayedUtc = now;
        string localDay = now.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        record.DailySeconds[localDay] = record.DailySeconds.GetValueOrDefault(localDay) + observedSeconds;
        _trackedGameSeconds += observedSeconds;
    }

    private void FinalizeGameTracking(DateTimeOffset end)
    {
        if (_trackedGamePlace is long id && _trackedGameSeconds > 0)
        {
            var record = GetOrCreateGame(id);
            _state.Stats.GameSessions.Insert(0, new ObservedGameSession
            {
                PlaceId = id, Name = record.Name, StartedUtc = _trackedGameStarted,
                EndedUtc = end, ObservedSeconds = _trackedGameSeconds
            });
            // Bounded history; aggregates remain even when old sessions are discarded.
            if (_state.Stats.GameSessions.Count > 500)
                _state.Stats.GameSessions.RemoveRange(500, _state.Stats.GameSessions.Count - 500);
        }
        ResetGameTracking();
    }

    private void RefreshGameHub(bool force = false)
    {
        if (!_gameHubReady || GameHubGamesGrid is null || _state?.Stats is null) return;
        var now = DateTimeOffset.UtcNow;
        if (!force && now - _lastHubRefresh < TimeSpan.FromSeconds(10)) return;
        _lastHubRefresh = now;
        var games = _state.Stats.Games;
        var ranked = games.Where(g => g.ObservedSeconds > 0)
            .OrderByDescending(g => g.ObservedSeconds).ThenBy(g => g.DisplayName).ToList();
        var favorites = games.Where(g => g.Favorite)
            .OrderByDescending(g => g.LastQuickLaunchUtc ?? DateTimeOffset.MinValue)
            .ThenBy(g => g.DisplayName).ToList();
        var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        HomeTodayTime.Text = DisplayDuration(_state.Stats.DailyObservedSeconds.GetValueOrDefault(today));
        HomeGamesCount.Text = ranked.Count.ToString(CultureInfo.InvariantCulture);
        HomeTopGameName.Text = ranked.FirstOrDefault()?.DisplayName ?? "No game data yet";
        HomeTopGamesList.ItemsSource = ranked.Take(5).ToList();
        HomeFavoriteQuickList.ItemsSource = favorites.Take(6).ToList();
        GameHubCount.Text = ranked.Count.ToString(CultureInfo.InvariantCulture);
        GameHubTracked.Text = DisplayDuration(ranked.Sum(g => g.ObservedSeconds));
        GameHubFavouriteCount.Text = favorites.Count.ToString(CultureInfo.InvariantCulture);
        UpdateGameTrackingIndicator();
        var selectedId = (GameHubGamesGrid.SelectedItem as GameActivityRecord)?.PlaceId;
        _updatingGameGrid = true;
        try
        {
            var matched = games.Where(g =>
                (!(_hubFavouritesOnly) || g.Favorite) &&
                (string.IsNullOrWhiteSpace(_gameHubFilter) ||
                 g.DisplayName.Contains(_gameHubFilter, StringComparison.OrdinalIgnoreCase) ||
                 g.PlaceId.ToString(CultureInfo.InvariantCulture).Contains(_gameHubFilter, StringComparison.OrdinalIgnoreCase)));
            var filtered = _hubSortMode switch
            {
                1 => matched.OrderByDescending(g => g.LastPlayedUtc ?? DateTimeOffset.MinValue).ThenBy(g => g.DisplayName).ToList(),
                2 => matched.OrderBy(g => g.DisplayName).ToList(),
                _ => matched.OrderByDescending(g => g.ObservedSeconds).ThenBy(g => g.DisplayName).ToList()
            };
            GameHubGamesGrid.ItemsSource = filtered;
            GameHubGamesGrid.SelectedItem = filtered.FirstOrDefault(g => g.PlaceId == selectedId) ?? filtered.FirstOrDefault();
            var oldFavoriteId = (GameHubFavoritesList.SelectedItem as GameActivityRecord)?.PlaceId;
            GameHubFavoritesList.ItemsSource = favorites;
            GameHubFavoritesList.SelectedItem = favorites.FirstOrDefault(g => g.PlaceId == oldFavoriteId);
            GameHubRecentGrid.ItemsSource = _state.Stats.GameSessions.Take(100).ToList();
        }
        finally { _updatingGameGrid = false; }
        RefreshSelectedGameInfo();
        RefreshPresenceGameChoices();
        BeginGameIconRefresh();
    }

    private void RefreshSelectedGameInfo()
    {
        if (!_gameHubReady) return;
        var game = GameHubGamesGrid.SelectedItem as GameActivityRecord;
        GameHubSelectedName.Text = game?.DisplayName ?? "Select a game above";
        GameHubSelectedImage.Source = game?.Icon;
        GameHubSelectedTime.Text = game?.PlaytimeLabel ?? "0m";
        GameHubSelectedSessionCount.Text = game?.Sessions.ToString(CultureInfo.InvariantCulture) ?? "0";
        var completeSessions = game is null ? new List<ObservedGameSession>()
            : _state.Stats.GameSessions.Where(row => row.PlaceId == game.PlaceId).ToList();
        GameHubSelectedRecentSessions.ItemsSource = completeSessions.Take(4).ToList();
        GameHubAverageSession.Text = game is not null && game.Sessions > 0 ? DisplayDuration(game.ObservedSeconds / game.Sessions) : "0m";
        GameHubLongestSession.Text = completeSessions.Count == 0 ? "—" : DisplayDuration(completeSessions.Max(s => s.ObservedSeconds));
        int range = GameHubRangeCombo?.SelectedIndex == 1 ? 30 : 7;
        var recentDays = Enumerable.Range(0, range).Select(i => DateTime.Today.AddDays(i - range + 1)).ToArray();
        long rangeSeconds = recentDays.Sum(d => game?.DailySeconds.GetValueOrDefault(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) ?? 0);
        GameHubRangeTotal.Text = DisplayDuration(rangeSeconds);
        GameHubRangeCaption.Text = $"LAST {range} DAYS  ·  OBSERVED TIME";
        GameHubSelectedDescription.Text = game is null ? "Play a Roblox experience to start collecting game-specific statistics."
            : $"Place ID: {game.PlaceId}  ·  {game.PlaytimeLabel} observed  ·  {game.Sessions} sessions  ·  Last: {game.LastPlayedLabel}";
        GameHubDailyBars.Children.Clear();
        var days = recentDays;
        long max = Math.Max(60, days.Max(day => game?.DailySeconds.GetValueOrDefault(day.ToString("yyyy-MM-dd")) ?? 0));
        foreach (var day in days)
        {
            long seconds = game?.DailySeconds.GetValueOrDefault(day.ToString("yyyy-MM-dd")) ?? 0;
            var col = new StackPanel { Width = range == 30 ? 29 : 42, VerticalAlignment = VerticalAlignment.Bottom };
            var value = new TextBlock { Text = range == 30 ? "" : seconds == 0 ? "–" : DisplayDuration(seconds),
                HorizontalAlignment = HorizontalAlignment.Center, FontSize = 9,
                Foreground = (Brush)Application.Current.Resources["Muted"], Margin = new Thickness(0, 0, 0, 5) };
            var bar = new Border { Width = range == 30 ? 17 : 24, Height = Math.Max(3, 67.0 * seconds / max),
                CornerRadius = new CornerRadius(4, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
                Background = (Brush)Application.Current.Resources["Accent"],
                ToolTip = day.ToString("ddd dd MMM") + ": " + DisplayDuration(seconds) };
            var label = new TextBlock { Text = range == 30 ? day.Day.ToString(CultureInfo.InvariantCulture) : day.ToString("ddd"), HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 10, Foreground = (Brush)Application.Current.Resources["Muted"], Margin = new Thickness(0, 6, 0, 0) };
            col.Children.Add(value); col.Children.Add(bar); col.Children.Add(label);
            GameHubDailyBars.Children.Add(col);
        }
    }

    private void GameHubRange_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_gameHubReady) RefreshSelectedGameInfo();
    }

    private void GameHubSort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_gameHubReady) return;
        _hubSortMode = Math.Max(0, GameHubSortCombo.SelectedIndex);
        RefreshGameHub(force: true);
    }

    private void GameHubFavoriteFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_gameHubReady) return;
        _hubFavouritesOnly = GameHubOnlyFavorites.IsChecked == true;
        RefreshGameHub(force: true);
    }

    private void GameSearch_Changed(object sender, TextChangedEventArgs e)
    {
        _gameHubFilter = GameSearchBox.Text.Trim();
        RefreshGameHub(force: true);
    }

    private void GameHubSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingGameGrid) RefreshSelectedGameInfo();
    }

    // A favourite changes only local metadata. Avoid Persist(), which refreshes
    // several unrelated WPF pages and then refreshes Game Hub again. That
    // redundant sequence could throw from a synchronous UI event handler.
    private bool TryChangeFavorite(GameActivityRecord game, bool favourite)
    {
        bool previous = game.Favorite;
        if (previous == favourite) return true;
        CrashDiagnostics.Mark("Favourite: click");
        try
        {
            game.Favorite = favourite;
            CrashDiagnostics.Mark("Favourite: flag updated");
            Storage.Save(_state);
            CrashDiagnostics.Mark("Favourite: state saved");
            RefreshGameHub(force: true);
            CrashDiagnostics.Mark("Favourite: Game Hub refreshed");
            SetStatus(favourite ? "Favourite saved." : "Favourite removed.");
            CrashDiagnostics.Mark("Favourite: completed");
            return true;
        }
        catch (Exception ex)
        {
            CrashDiagnostics.Record("Favourite operation", ex);
            game.Favorite = previous;
            // Attempt to restore the original local state. Preserve the
            // original exception for the user if rollback also fails.
            try { Storage.Save(_state); } catch { }
            SetStatus("Could not update favourite: " + ex.Message);
            MessageBox.Show(this,
                "Viora couldn't update that favourite. No Roblox files were changed.\n\n" +
                ex.GetType().Name + ": " + ex.Message,
                "Viora — Favourite error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (GameHubGamesGrid.SelectedItem is not GameActivityRecord game)
        { SetStatus("Select an experience first."); return; }
        TryChangeFavorite(game, !game.Favorite);
    }

    private static bool TryParseRobloxPlace(string input, out long placeId)
    {
        placeId = 0;
        input = input.Trim();
        if (long.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out placeId)) return placeId > 0;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("www.roblox.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("roblox.com", StringComparison.OrdinalIgnoreCase))) return false;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        return parts.Length >= 2 && parts[0].Equals("games", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out placeId) && placeId > 0;
    }

    private async void AddFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseRobloxPlace(GameHubAddUrl.Text, out long id))
        { Alert("Enter a Roblox place ID or an official Roblox game link."); return; }

        try
        {
            var game = GetOrCreateGame(id);
            if (!TryChangeFavorite(game, true)) return;
            GameHubAddUrl.Clear();
            SetStatus("Favourite added. Looking up its public title...");
            var name = await _robloxGameNames.GetNameAsync(id);
            UpdateGameName(id, name);
            // A name lookup can complete much later; write the resolved name
            // without refreshing every unrelated page from an async-void handler.
            Storage.Save(_state);
            SetStatus(name is null ? $"Favourite saved as Place #{id} (name unavailable)." : $"Favourite added: {name}");
        }
        catch (Exception ex)
        {
            SetStatus("Could not add favourite: " + ex.Message);
            MessageBox.Show(this,
                "Viora couldn't finish adding the favourite.\n\n" +
                ex.GetType().Name + ": " + ex.Message,
                "Viora — Favourite error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string OfficialGameUrl(long placeId) => $"https://www.roblox.com/games/{placeId}";

    private void OpenGamePage(long placeId)
    {
        // Open only a generated official Roblox URL, never an arbitrary user-supplied destination.
        Process.Start(new ProcessStartInfo(OfficialGameUrl(placeId)) { UseShellExecute = true });
        var game = GetOrCreateGame(placeId);
        game.QuickLaunchRequests++;
        game.LastQuickLaunchUtc = DateTimeOffset.UtcNow;
        Persist();
        SetStatus($"Opened {game.DisplayName}'s official Roblox page. Joining is handled by Roblox.");
    }

    private void CopyOfficialGameLink(long? placeId)
    {
        if (placeId is not > 0) { SetStatus("Choose a game to copy its link."); return; }
        try { Clipboard.SetText(OfficialGameUrl(placeId.Value)); SetStatus("Official Roblox game link copied."); }
        catch (Exception ex) { Alert("Couldn't copy the link: " + ex.Message); }
    }

    private void CopySelectedGameLink_Click(object sender, RoutedEventArgs e) =>
        CopyOfficialGameLink((GameHubGamesGrid.SelectedItem as GameActivityRecord)?.PlaceId);

    private void CopyFavoriteGameLink_Click(object sender, RoutedEventArgs e) =>
        CopyOfficialGameLink((GameHubFavoritesList.SelectedItem as GameActivityRecord)?.PlaceId);

    private void DashboardQuickLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not long placeId || placeId <= 0) return;
        try { OpenGamePage(placeId); }
        catch (Exception ex) { Alert("Couldn't open Roblox: " + ex.Message); }
    }

    private void OpenSelectedGame_Click(object sender, RoutedEventArgs e)
    {
        if (GameHubGamesGrid.SelectedItem is not GameActivityRecord game)
        { SetStatus("Select a game first."); return; }
        try { OpenGamePage(game.PlaceId); }
        catch (Exception ex) { Alert("Couldn't open Roblox: " + ex.Message); }
    }

    private void LaunchFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (GameHubFavoritesList.SelectedItem is not GameActivityRecord game)
        { SetStatus("Select a favourite first."); return; }
        try { OpenGamePage(game.PlaceId); }
        catch (Exception ex) { Alert("Couldn't open Roblox: " + ex.Message); }
    }

    private void RemoveFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (GameHubFavoritesList.SelectedItem is not GameActivityRecord game)
        { SetStatus("Select a favourite first."); return; }
        TryChangeFavorite(game, false);
    }

    private void ExportGameHistory_Click(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog { FileName = "viora-game-history.csv", Filter = "CSV files (*.csv)|*.csv" };
        if (picker.ShowDialog() != true) return;
        try
        {
            var csv = new StringBuilder("Place ID,Name,Observed Seconds,Observed Sessions,Favourite,Last Played UTC\r\n");
            foreach (var g in _state.Stats.Games)
                csv.AppendLine(string.Join(",", g.PlaceId, Csv(g.Name), g.ObservedSeconds, g.Sessions,
                    g.Favorite ? "yes" : "no", Csv(g.LastPlayedUtc?.ToString("O") ?? "")));
            csv.AppendLine();
            csv.AppendLine("Session log");
            csv.AppendLine("Place ID,Name,Start UTC,End UTC,Observed Seconds");
            foreach (var session in _state.Stats.GameSessions)
                csv.AppendLine(string.Join(",", session.PlaceId, Csv(session.Name), Csv(session.StartedUtc.ToString("O")),
                    Csv(session.EndedUtc.ToString("O")), session.ObservedSeconds));
            File.WriteAllText(picker.FileName, csv.ToString(), new UTF8Encoding(true));
            SetStatus("Game activity exported.");
        }
        catch (Exception ex) { Alert("Couldn't export game activity: " + ex.Message); }
    }

    private void RefreshPresenceGameChoices()
    {
        if (!_gameHubReady || DiscordPerGameCombo is null) return;
        var choices = _state.Stats.Games.OrderBy(g => g.DisplayName).ToList();
        long? oldId = (DiscordPerGameCombo.SelectedItem as GameActivityRecord)?.PlaceId;
        if (oldId is not null && choices.Count == DiscordPerGameCombo.Items.Count &&
            choices.Select(g => g.PlaceId).SequenceEqual(DiscordPerGameCombo.Items.OfType<GameActivityRecord>().Select(g => g.PlaceId))) return;
        _loadingPresenceFields = true;
        DiscordPerGameCombo.ItemsSource = choices;
        DiscordPerGameCombo.SelectedItem = choices.FirstOrDefault(g => g.PlaceId == oldId) ?? choices.FirstOrDefault();
        _loadingPresenceFields = false;
        LoadPresenceFields();
    }

    private void DiscordPerGameSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingPresenceFields) LoadPresenceFields();
    }

    private void LoadPresenceFields()
    {
        if (!_gameHubReady || DiscordPerGameDetails is null) return;
        var game = DiscordPerGameCombo.SelectedItem as GameActivityRecord;
        var preset = _state.GamePresencePresets.FirstOrDefault(p => p.PlaceId == game?.PlaceId);
        DiscordPerGameDetails.Text = preset?.Details ?? "";
        DiscordPerGameState.Text = preset?.State ?? "";
        DiscordPerGameImage.Text = preset?.LargeImageKey ?? "";
        DiscordPerGameEnabled.IsChecked = preset?.Enabled ?? true;
        UpdatePresenceTemplatePreview();
    }

    private static string ExpandGameTemplate(string value, string gameName)
    {
        var expanded = value.Replace("{game}", gameName, StringComparison.OrdinalIgnoreCase).Trim();
        return expanded.Length <= 128 ? expanded : expanded[..128];
    }

    private void SaveGamePresence_Click(object sender, RoutedEventArgs e)
    {
        if (DiscordPerGameCombo.SelectedItem is not GameActivityRecord game)
        { Alert("Add or play a game in Game Hub first, then select it here."); return; }
        var preset = _state.GamePresencePresets.FirstOrDefault(p => p.PlaceId == game.PlaceId);
        if (preset is null)
        {
            preset = new GamePresencePreset { PlaceId = game.PlaceId };
            _state.GamePresencePresets.Add(preset);
        }
        preset.Details = DiscordPerGameDetails.Text.Trim();
        preset.State = DiscordPerGameState.Text.Trim();
        preset.LargeImageKey = DiscordPerGameImage.Text.Trim();
        preset.Enabled = DiscordPerGameEnabled.IsChecked == true;
        Persist();
        _lastActivityKey = "";
        RefreshDiscordPreview();
        UpdatePresenceTemplatePreview();
        _ = PublishDiscordNowAsync(force: true);
        SetStatus("Per-game Discord presence saved.");
    }

    private void ClearGamePresence_Click(object sender, RoutedEventArgs e)
    {
        if (DiscordPerGameCombo.SelectedItem is not GameActivityRecord game) return;
        _state.GamePresencePresets.RemoveAll(p => p.PlaceId == game.PlaceId);
        LoadPresenceFields();
        Persist();
        _lastActivityKey = "";
        RefreshDiscordPreview();
        _ = PublishDiscordNowAsync(force: true);
        SetStatus("Per-game override removed.");
    }
}
