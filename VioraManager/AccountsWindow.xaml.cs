using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace VioraManager;

// These are shortcuts, not Roblox authentication profiles. The user's
// official browser owns authentication; Viora never sees passwords/cookies.
public sealed class SavedRobloxAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Username { get; set; } = "";
    public string Browser { get; set; } = "Edge";
    public string ProfileDirectory { get; set; } = "Default";
    public long? PlaceId { get; set; }
    // Version-tolerant: existing account shortcuts default to false.
    // A true value points to an isolated browser profile derived from Id.
    // No cookies/passwords/tokens are stored in this JSON record.
    public bool ManagedBrowserProfile { get; set; }

    [JsonIgnore]
    public string Detail => Browser + (ManagedBrowserProfile
        ? " · Private browser profile" : " · " + ProfileDirectory) +
        (PlaceId is long id ? " · Game " + id : " · No saved game");
}

public partial class AccountsWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Regex UsernamePattern = new(@"^[A-Za-z0-9_]{3,20}$", RegexOptions.CultureInvariant);
    private static readonly Regex ProfilePattern = new(@"^(Default|Profile [1-9][0-9]{0,3})$", RegexOptions.CultureInvariant);

    private readonly ObservableCollection<SavedRobloxAccount> _accounts = new();
    private string? _selectedId;
    private SavedRobloxAccount? _pendingLoginAccount;
    private bool _readOnly;
    private readonly string _storagePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Viora", "Manager", "accounts.json");

    public AccountsWindow(bool openLoginOnLaunch = false)
    {
        InitializeComponent();
        for (int i = 0; i <= 25; i++)
            ProfileInput.Items.Add(i == 0 ? "Default" : "Profile " + i);
        ProfileInput.SelectedIndex = 0;
        AccountList.ItemsSource = _accounts;
        LoadShortcuts();
        RefreshEmptyState();
        if (openLoginOnLaunch)
        {
            Loaded += (_, _) =>
            {
                if (_pendingLoginAccount is null)
                    BeginBrowserLogin();
            };
        }
    }

    private void LoadShortcuts()
    {
        if (!File.Exists(_storagePath)) return;
        try
        {
            var json = File.ReadAllText(_storagePath);
            var entries = JsonSerializer.Deserialize<List<SavedRobloxAccount>>(json, JsonOptions);
            if (entries is null || entries.Count > 1000) throw new InvalidDataException("Invalid account file.");
            var seenUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (!IsValid(entry) || !seenUsers.Add(entry.Username) ||
                    !seenIds.Add(entry.Id) ||
                    (!entry.ManagedBrowserProfile &&
                     !seenProfiles.Add(entry.Browser + ":" + entry.ProfileDirectory)))
                    throw new InvalidDataException("Invalid or duplicate shortcut.");
                _accounts.Add(entry);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            // Do not overwrite a partially corrupt account file. The entire file
            // stays on disk for recovery. Read-only mode makes this explicit.
            _accounts.Clear();
            _readOnly = true;
            SaveButton.IsEnabled = false;
            StartBrowserLoginButton.IsEnabled = false;
            SaveMessage.Text = "Saved accounts could not be loaded. Original file preserved.";
            MessageBox.Show(this,
                "Your saved account shortcuts couldn't be loaded. The original file has been preserved, " +
                "and editing is disabled in this window until it is repaired or restored.\n\n" + ex.Message,
                "Viora account shortcuts", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static bool IsValid(SavedRobloxAccount? a) =>
        a is not null && Guid.TryParseExact(a.Id, "N", out _) &&
        a.Username is not null && UsernamePattern.IsMatch(a.Username) &&
        a.Browser is "Edge" or "Chrome" or "Brave" &&
        a.ProfileDirectory is not null &&
        (a.ManagedBrowserProfile || ProfilePattern.IsMatch(a.ProfileDirectory)) &&
        (a.PlaceId is null or > 0);

    private void StartBrowserLogin_Click(object sender, RoutedEventArgs e) =>
        BeginBrowserLogin();

    private void BeginBrowserLogin()
    {
        if (_readOnly) return;
        if (_accounts.Count >= 1000)
        {
            LoginStatusText.Text = "The account shortcut limit has been reached.";
            return;
        }
        if (_pendingLoginAccount is not null)
        {
            if (MessageBox.Show(this,
                "Start a fresh Roblox login? The previous unfinished browser profile will " +
                "remain on disk until you clean it up manually.",
                "Start another login", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        }

        string browser = (BrowserInput.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Edge";
        var pending = new SavedRobloxAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            Browser = browser,
            ProfileDirectory = "Default",
            ManagedBrowserProfile = true
        };
        try
        {
            // Roblox's own site handles the password and MFA. We deliberately
            // do not inspect browser storage, Roblox cookies, or browser requests.
            RobloxBrowserLauncher.OpenLogin(pending);
            _pendingLoginAccount = pending;
            _selectedId = null;
            AccountList.SelectedItem = null;
            UsernameInput.Clear();
            PlaceInput.Clear();
            BrowserInput.IsEnabled = false;
            ProfileInput.Text = "Private browser (managed by Viora)";
            ProfileInput.IsEnabled = false;
            SaveButton.Content = "Finish && save account";
            LoginStatusText.Text =
                "Roblox login has opened in a fresh private browser profile. " +
                "Sign in on roblox.com, then return here, enter the Roblox username " +
                "as a label and click Finish & save. Viora doesn't check the browser's identity.";
            SaveMessage.Text = "Your browser stores its own login; Viora saves only this account shortcut.";
            UsernameInput.Focus();
        }
        catch (Exception ex) when (ex is ArgumentException or
            System.ComponentModel.Win32Exception or
            FileNotFoundException or InvalidOperationException)
        {
            LoginStatusText.Text = "Couldn't open Roblox login. Choose an installed browser and try again.";
            MessageBox.Show(this, ex.Message, "Open Roblox login",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshEmptyState() =>
        EmptyText.Visibility = _accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private bool Persist()
    {
        if (_readOnly) return false;
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_storagePath)!;
            Directory.CreateDirectory(directory);
            temporaryPath = _storagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_accounts.ToList(), JsonOptions));
            if (File.Exists(_storagePath)) File.Copy(_storagePath, _storagePath + ".bak", true);
            File.Move(temporaryPath, _storagePath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveMessage.Text = "Unable to save shortcuts: " + ex.Message;
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (IOException) { }
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        try
        {
            string username = UsernameInput.Text.Trim();
            long? place = RobloxBrowserLauncher.ParsePlaceId(PlaceInput.Text);
            var current = _accounts.FirstOrDefault(x => x.Id == _selectedId);
            var pending = current is null ? _pendingLoginAccount : null;

            // For Viora-managed browser profiles, lock the browser and profile
            // to the original login folder. A visible label cannot authenticate
            // an account or transfer its browser's cookies to another profile.
            string browser = current?.ManagedBrowserProfile == true
                ? current.Browser
                : pending?.Browser ??
                  (BrowserInput.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Edge";
            string profile = current?.ManagedBrowserProfile == true || pending is not null
                ? "Default" : ProfileInput.Text.Trim();
            bool managed = current?.ManagedBrowserProfile ?? pending is not null;
            if (!UsernamePattern.IsMatch(username))
                throw new ArgumentException(
                    "After signing into Roblox, enter a username of 3–20 letters, numbers or underscores.");
            if (browser is not ("Edge" or "Chrome" or "Brave") ||
                (!managed && !ProfilePattern.IsMatch(profile)))
                throw new ArgumentException("Choose a supported browser and profile.");

            if (_accounts.Any(a => a.Id != _selectedId &&
                (a.Username.Equals(username, StringComparison.OrdinalIgnoreCase) ||
                 (!a.ManagedBrowserProfile && !managed &&
                  a.Browser == browser && a.ProfileDirectory == profile))))
                throw new ArgumentException(
                    "That username or browser profile already has a shortcut.");

            if (current is null && _accounts.Count >= 1000)
                throw new ArgumentException("The account shortcut limit of 1,000 has been reached.");

            bool editing = current is not null;
            var updated = new SavedRobloxAccount
            {
                Id = current?.Id ?? pending?.Id ?? Guid.NewGuid().ToString("N"),
                Username = username,
                Browser = browser,
                ProfileDirectory = profile,
                PlaceId = place,
                ManagedBrowserProfile = managed
            };

            int index = current is null ? -1 : _accounts.IndexOf(current);
            if (index >= 0) _accounts[index] = updated;
            else _accounts.Add(updated);
            if (!Persist())
            {
                if (index >= 0 && current is not null) _accounts[index] = current;
                else _accounts.Remove(updated);
                return;
            }

            bool fromLogin = pending is not null;
            ClearFields();
            SaveMessage.Text = fromLogin
                ? "Account shortcut saved. Roblox's browser profile keeps its own login. " +
                  "The username is your label and has not been verified by Viora."
                : editing ? "Account shortcut updated." : "Account shortcut saved.";
            RefreshEmptyState();
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "Check account details",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountList.SelectedItem is not SavedRobloxAccount account) return;
        _pendingLoginAccount = null;
        _selectedId = account.Id;
        UsernameInput.Text = account.Username;
        PlaceInput.Text = account.PlaceId?.ToString() ?? "";
        BrowserInput.SelectedIndex = account.Browser switch
        {
            "Chrome" => 1, "Brave" => 2, _ => 0
        };
        BrowserInput.IsEnabled = !account.ManagedBrowserProfile;
        ProfileInput.IsEnabled = !account.ManagedBrowserProfile;
        ProfileInput.Text = account.ManagedBrowserProfile
            ? "Private browser (managed by Viora)" : account.ProfileDirectory;
        SaveButton.Content = "Save changes";
        LoginStatusText.Text = account.ManagedBrowserProfile
            ? "This shortcut uses an isolated browser login. Open it with Sign in / Roblox below."
            : "This is a legacy browser profile shortcut. You can still edit it.";
        SaveMessage.Text = "Editing " + account.Username +
            ". Roblox identity is controlled by the browser, not by this label.";
    }

    private void ClearFields()
    {
        _pendingLoginAccount = null;
        _selectedId = null;
        AccountList.SelectedItem = null;
        UsernameInput.Clear();
        PlaceInput.Clear();
        BrowserInput.IsEnabled = true;
        ProfileInput.IsEnabled = true;
        BrowserInput.SelectedIndex = 0;
        ProfileInput.SelectedIndex = 0;
        SaveButton.Content = "Save account";
        LoginStatusText.Text = "Choose a browser below, then open Roblox login.";
        RefreshEmptyState();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ClearFields();
        SaveMessage.Text = "";
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly || (sender as Button)?.Tag is not SavedRobloxAccount account) return;
        if (MessageBox.Show(this,
            "Remove the shortcut for " + account.Username +
            "?\n\nThis does NOT delete or log out of the Roblox account. " +
            "If this shortcut uses a private browser profile, its local browser data remains until you delete it separately.",
            "Remove saved shortcut", MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        int index = _accounts.IndexOf(account);
        if (index < 0) return;
        _accounts.RemoveAt(index);
        if (!Persist())
        {
            _accounts.Insert(index, account);
            return;
        }
        ClearFields();
        SaveMessage.Text = "Shortcut removed. Browser login was left untouched.";
    }

    private void OpenAccount_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is SavedRobloxAccount account)
            OpenOfficialRoblox(account, null);
    }

    private void OpenGame_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SavedRobloxAccount account) return;
        if (account.PlaceId is not long place)
        {
            MessageBox.Show(this, "Edit this shortcut and enter a Roblox game place ID first.",
                "No game selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenOfficialRoblox(account, place);
    }

    private void OpenOfficialRoblox(SavedRobloxAccount account, long? placeId)
    {
        try
        {
            RobloxBrowserLauncher.Open(account, placeId, tryJoinDirectly: placeId.HasValue);
            SaveMessage.Text = placeId.HasValue
                ? "Opening game-start link for " + account.Username +
                  ". Confirm which Roblox Player account actually launched."
                : "Opened official Roblox website in " + account.Browser + " (" +
                  account.ProfileDirectory + "). Sign in to the correct account there.";
        }
        catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception or
            FileNotFoundException or InvalidOperationException)
        {
            MessageBox.Show(this, "Could not launch Roblox page: " + ex.Message,
                "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
