using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace VioraManager;

public partial class MainWindow
{
    private readonly ObservableCollection<SavedRobloxAccount> _connectedAccounts = new();
    private bool _launchAccountWarningAccepted;

    private static readonly string AccountsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Viora", "Manager", "accounts.json");

    private void SetupConnectedAccounts()
    {
        ConnectedAccountsList.ItemsSource = _connectedAccounts;
        ReloadConnectedAccounts();
        _ = RefreshPublicProfilesAsync();
    }

    private void ReloadConnectedAccounts()
    {
        _connectedAccounts.Clear();
        try
        {
            if (File.Exists(AccountsFile))
            {
                var file = new FileInfo(AccountsFile);
                if (file.Length > 1024 * 1024)
                    throw new InvalidDataException("Saved shortcuts file exceeds 1 MB.");
                var records = JsonSerializer.Deserialize<List<SavedRobloxAccount>>(
                    File.ReadAllText(AccountsFile)) ??
                    throw new InvalidDataException("Saved shortcuts file is invalid.");
                if (records.Count > 1000)
                    throw new InvalidDataException("Account shortcut file exceeds the 1000-entry safety limit.");
                foreach (var item in records)
                {
                    // Avoid attempting to use arbitrary edited browser paths/args.
                    if (item is null ||
                        item.Browser is not ("Chrome" or "Edge" or "Brave") ||
                        string.IsNullOrWhiteSpace(item.Username) ||
                        item.PlaceId is <= 0)
                        throw new InvalidDataException("A saved shortcut has invalid data.");
                    _connectedAccounts.Add(item);
                }
            }
            SavedAccountCount.Text = _connectedAccounts.Count + " saved";
            EmptyAccountsText.Visibility = _connectedAccounts.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
            AccountLaunchStatus.Text = "Browser logins are separate, but Roblox Player may use a different account. Public profile images aren't login verification.";
            ConfigureAccountView();
        }
        catch (Exception ex) when (ex is JsonException or IOException or
            UnauthorizedAccessException or InvalidDataException)
        {
            _connectedAccounts.Clear();
            SavedAccountCount.Text = "Load error";
            EmptyAccountsText.Visibility = Visibility.Visible;
            EmptyAccountsText.Text = "Saved accounts could not be read; the existing file was preserved. Open Edit accounts for recovery.";
            AccountLaunchStatus.Text = "Accounts unavailable: " + ex.Message;
            UpdateAccountSummary();
        }
    }

    private void SavedAccountLogin_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SavedRobloxAccount account) return;
        if (!account.EmbeddedBrowserProfile &&
            MessageBox.Show(this,
                "This account was saved using an external Edge/Chrome browser. " +
                "Viora's new embedded browser keeps its own separate login. " +
                "Sign in once more inside Viora to detect the account automatically? " +
                "Your existing browser session will remain unchanged.",
                "Connect inside Viora", MessageBoxButton.YesNo,
                MessageBoxImage.Information) != MessageBoxResult.Yes)
            return;
        try
        {
            var login = new RobloxEmbeddedLoginWindow(account.Id) { Owner = this };
            if (login.ShowDialog() == true && login.DetectedUser is not null)
            {
                var saved = EmbeddedRobloxAccountStore.SaveDetectedAccount(account.Id, login.DetectedUser);
                ReloadConnectedAccounts();
                _ = RefreshPublicProfilesAsync();
                AccountLaunchStatus.Text = "Connected @" + saved.RobloxUsername +
                    " · Roblox User ID " + saved.PublicUserId +
                    ". Roblox's browser session is saved for this account.";
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
            InvalidDataException or UnauthorizedAccessException or IOException or JsonException)
        {
            MessageBox.Show(this, ex.Message, "Viora Manager — Roblox login",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SavedAccountPlay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SavedRobloxAccount account) return;
        long? place;
        try
        {
            // Explicit typed override applies to every Play button. Otherwise,
            // use the game the user saved specifically for this account.
            place = string.IsNullOrWhiteSpace(QuickPlaceInput.Text)
                ? account.PlaceId
                : RobloxBrowserLauncher.ParsePlaceId(QuickPlaceInput.Text);
            if (place is null or <= 0)
                throw new ArgumentException("Enter a game/subplace ID above or save a game ID for this account.");

            if (!_launchAccountWarningAccepted)
            {
                var confirmation = MessageBox.Show(this,
                    "Viora will open Roblox's official game-start link for " + account.Username +
                    " (Place ID " + place + ") in " + account.Browser + " / " + account.ProfileDirectory +
                    ".\n\nThis may open Roblox Player automatically, but Roblox Player can use an already-active account " +
                    "instead of the browser account. Multiple Roblox clients are experimental and not guaranteed.\n\n" +
                    "Check which account Roblox actually launches before playing. Continue?",
                    "Check the Roblox Player account", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (confirmation != MessageBoxResult.Yes) return;
                _launchAccountWarningAccepted = true;
            }

            string? jobId = GetOptionalJobId();
            if (account.EmbeddedBrowserProfile)
            {
                // Same browser profile used during Add Account; Roblox itself
                // still determines which account the desktop Player uses.
                var game = new RobloxEmbeddedLoginWindow(account.Id,
                    RobloxBrowserLauncher.BuildGameStartUrl(place.Value, jobId),
                    detectLogin: false) { Owner = this };
                game.Show();
            }
            else
            {
                RobloxBrowserLauncher.Open(account, place, tryJoinDirectly: true, jobId: jobId);
            }
            RecordLaunch(account, place, jobId);
            AccountLaunchStatus.Text = "Opened game-start link for " + account.Username +
                " · Place " + place + ". Check Roblox Player to confirm the account and game.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
            FileNotFoundException or Win32Exception)
        {
            MessageBox.Show(this, ex.Message, "Viora Manager — Play",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
