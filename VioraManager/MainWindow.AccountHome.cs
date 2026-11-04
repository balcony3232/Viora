using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace VioraManager;

public partial class MainWindow
{
    private CancellationTokenSource? _accountLaunchCancel;
    private bool _profileRefreshBusy;

    private void AccountsHome_Click(object sender, RoutedEventArgs e) => SwitchHome(true);
    private void InstancesHome_Click(object sender, RoutedEventArgs e) => SwitchHome(false);

    private void SwitchHome(bool accounts)
    {
        AccountsHome.Visibility = accounts ? Visibility.Visible : Visibility.Collapsed;
        InstancesHome.Visibility = accounts ? Visibility.Collapsed : Visibility.Visible;
        AccountsTab.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                accounts ? "#A982F4" : "#39274D"));
        InstancesTab.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                accounts ? "#39274D" : "#A982F4"));
        AccountsTab.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                accounts ? "#180D29" : "#F2EAFF"));
        InstancesTab.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                accounts ? "#F2EAFF" : "#180D29"));
        Title = accounts ? "Viora Manager — Accounts" : "Viora Manager — Instances & Tools";
    }

    private void AccountSearch_Changed(object sender, TextChangedEventArgs e) =>
        RefreshAccountView();

    private void AccountFilter_Changed(object sender, RoutedEventArgs e) =>
        RefreshAccountView();

    private void AccountsSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedAccountCount is not null && ConnectedAccountsList is not null)
            SelectedAccountCount.Text = ConnectedAccountsList.SelectedItems.Count.ToString();
    }

    private void ConfigureAccountView()
    {
        var view = CollectionViewSource.GetDefaultView(_connectedAccounts);
        if (view is null) return;
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(nameof(SavedRobloxAccount.IsPinned),
            ListSortDirection.Descending));
        view.SortDescriptions.Add(new SortDescription(nameof(SavedRobloxAccount.IsFavorite),
            ListSortDirection.Descending));
        view.SortDescriptions.Add(new SortDescription(nameof(SavedRobloxAccount.DisplayLabel),
            ListSortDirection.Ascending));
        view.Filter = MatchesAccountFilter;
        RebuildGroupFilter();
        UpdateAccountSummary();
    }

    private void RefreshAccountView()
    {
        if (ConnectedAccountsList is null) return;
        CollectionViewSource.GetDefaultView(_connectedAccounts)?.Refresh();
        UpdateAccountSummary();
    }

    private bool MatchesAccountFilter(object value)
    {
        if (value is not SavedRobloxAccount item) return false;
        if (AccountSearchInput is not null &&
            !string.IsNullOrWhiteSpace(AccountSearchInput.Text) &&
            !item.SearchableText.Contains(AccountSearchInput.Text.Trim(),
                StringComparison.OrdinalIgnoreCase))
            return false;
        if (AccountFavoriteFilter?.IsChecked == true && !item.IsFavorite) return false;
        if (AccountPinnedFilter?.IsChecked == true && !item.IsPinned) return false;

        string? selectedGroup = (AccountGroupFilter?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (!string.IsNullOrWhiteSpace(selectedGroup) &&
            !string.Equals(selectedGroup, item.Group, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private void RebuildGroupFilter()
    {
        if (AccountGroupFilter is null) return;
        string selected = (AccountGroupFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        AccountGroupFilter.Items.Clear();
        var all = new ComboBoxItem { Content = "All groups", Tag = "" };
        AccountGroupFilter.Items.Add(all);
        foreach (string group in _connectedAccounts
            .Select(x => x.Group.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            AccountGroupFilter.Items.Add(new ComboBoxItem { Content = group, Tag = group });
        }
        AccountGroupFilter.SelectedItem = AccountGroupFilter.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(x => string.Equals(x.Tag?.ToString(), selected,
                StringComparison.OrdinalIgnoreCase)) ?? all;
    }

    private void UpdateAccountSummary()
    {
        if (SavedAccountCount is not null)
            SavedAccountCount.Text = _connectedAccounts.Count + " saved";
        if (LinkedAccountCount is not null)
            LinkedAccountCount.Text = _connectedAccounts.Count(a => a.PublicUserId is > 0).ToString();
        if (EmptyAccountsText is not null)
            EmptyAccountsText.Visibility = _connectedAccounts.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
        if (SelectedAccountCount is not null && ConnectedAccountsList is not null)
            SelectedAccountCount.Text = ConnectedAccountsList.SelectedItems.Count.ToString();
    }

    private async void RefreshPublicProfiles_Click(object sender, RoutedEventArgs e) =>
        await RefreshPublicProfilesAsync();

    private async Task RefreshPublicProfilesAsync()
    {
        if (_profileRefreshBusy) return;
        _profileRefreshBusy = true;
        try
        {
            int matched = 0, tried = 0;
            // Modest per-request limit to avoid flooding Roblox public APIs and
            // to keep the app usable even if hundreds of shortcuts were saved.
            foreach (var account in _connectedAccounts.Take(60))
            {
                string realUsername = account.RobloxUsername.Trim();
                if (realUsername.Length == 0 &&
                    !Regex.IsMatch(account.Username, @"^Account\d+$",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    realUsername = account.Username;
                if (realUsername.Length == 0) continue;
                tried++;
                var profile = await RobloxPublicProfiles.LookupAsync(realUsername);
                if (profile is not null)
                {
                    account.ApplyPublicProfile(profile);
                    matched++;
                    // This is public profile data, not verified login identity.
                }
                await Task.Delay(90);
            }
            AccountLaunchStatus.Text = "Public profile lookup: " + matched + "/" + tried +
                " resolved. Avatar and display name do NOT verify the signed-in browser account.";
            RefreshAccountView();
        }
        catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
        {
            AccountLaunchStatus.Text = "Public profile lookup incomplete: " + ex.Message;
        }
        finally
        {
            _profileRefreshBusy = false;
        }
    }

    private void SelectAllAccounts_Click(object sender, RoutedEventArgs e) =>
        ConnectedAccountsList.SelectAll();

    private void ClearAccountSelection_Click(object sender, RoutedEventArgs e) =>
        ConnectedAccountsList.UnselectAll();

    private void EditSavedAccount_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SavedRobloxAccount account) return;
        var editor = new AccountsWindow(selectAccountId: account.Id) { Owner = this };
        editor.ShowDialog();
        ReloadConnectedAccounts();
    }

    private void ExportAccounts_Click(object sender, RoutedEventArgs e)
    {
        if (_connectedAccounts.Count == 0)
        {
            MessageBox.Show(this, "No saved account shortcuts to export.", "Viora Manager");
            return;
        }
        if (MessageBox.Show(this,
            "Export only Viora's account labels, groups, notes, saved games and browser-folder references?\n\n" +
            "Browser login sessions are NOT included; the export is plain JSON and may expose personal notes. " +
            "Don't share it publicly.",
            "Export account shortcuts", MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export Viora account shortcuts (no Roblox cookies)",
            Filter = "JSON backup (*.json)|*.json",
            FileName = "Viora-Accounts-Metadata.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(
                _connectedAccounts.ToList(), new JsonSerializerOptions { WriteIndented = true }));
            AccountLaunchStatus.Text = "Exported metadata only. Browser sign-ins were not exported.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Cannot export account metadata: " + ex.Message,
                "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void LaunchSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = ConnectedAccountsList.SelectedItems.Cast<SavedRobloxAccount>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select one or more account rows first (Ctrl+click or Shift+click).",
                "Launch selected accounts", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (selected.Count > 10)
        {
            MessageBox.Show(this, "Launch at most ten profiles per queue to avoid overwhelming Roblox or Windows.",
                "Queue limit", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_accountLaunchCancel is not null) return;
        long? place;
        string? job;
        try
        {
            place = RobloxBrowserLauncher.ParsePlaceId(QuickPlaceInput.Text);
            job = GetOptionalJobId();
            if (selected.Any(x => place is null && x.PlaceId is null))
                throw new ArgumentException("Provide a Place ID or save a game ID for every selected account.");
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid launch options", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(this,
            "Open the game-start link in " + selected.Count + " browser profiles, one at a time?\n\n" +
            "Roblox Player can still use an already-active account and may not open separate instances. " +
            "Verify each actual client before continuing.",
            "Launch selected", MessageBoxButton.YesNo,
            MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        using var cancel = new CancellationTokenSource();
        _accountLaunchCancel = cancel;
        LaunchSelectedButton.IsEnabled = false;
        CancelBatchButton.IsEnabled = true;
        try
        {
            int completed = 0;
            foreach (var account in selected)
            {
                cancel.Token.ThrowIfCancellationRequested();
                RobloxBrowserLauncher.Open(account, place ?? account.PlaceId,
                    tryJoinDirectly: true, jobId: job);
                RecordLaunch(account, place ?? account.PlaceId, job);
                completed++;
                AccountLaunchStatus.Text = "Opened " + completed + "/" + selected.Count +
                    " browser game-start links. Verify desktop Roblox identity.";
                if (completed < selected.Count)
                    await Task.Delay(TimeSpan.FromSeconds(LaunchDelaySeconds), cancel.Token);
            }
        }
        catch (OperationCanceledException)
        {
            AccountLaunchStatus.Text = "Launch queue cancelled. Already-opened browser windows remain open.";
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or
            InvalidOperationException or Win32Exception)
        {
            AccountLaunchStatus.Text = "Queue stopped: " + ex.Message;
        }
        finally
        {
            _accountLaunchCancel = null;
            LaunchSelectedButton.IsEnabled = true;
            CancelBatchButton.IsEnabled = false;
        }
    }

    private void CancelLaunchQueue_Click(object sender, RoutedEventArgs e) =>
        _accountLaunchCancel?.Cancel();

    private void BrowseServers_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var place = RobloxBrowserLauncher.ParsePlaceId(QuickPlaceInput.Text);
            if (place is null)
            {
                MessageBox.Show(this,
                    "Enter a game or subplace Place ID in the main box first.",
                    "Browse servers", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var picker = new ServerBrowserWindow(place.Value) { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedJobId is string job)
            {
                QuickJobInput.Text = job;
                AccountLaunchStatus.Text = "Selected public server " + job +
                    ". Select an account and press Play to attempt joining it.";
            }
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid Place ID",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private string? GetOptionalJobId()
    {
        var input = QuickJobInput.Text.Trim();
        if (input.Length == 0) return null;
        if (!Guid.TryParse(input, out var guid))
            throw new ArgumentException("Server Job ID must be a valid GUID.");
        return guid.ToString();
    }

    private void EndAccountView()
    {
        _accountLaunchCancel?.Cancel();
    }
}
