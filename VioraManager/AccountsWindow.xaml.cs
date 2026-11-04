using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace VioraManager;

// These are shortcuts, not Roblox authentication profiles. The user's
// official browser owns authentication; Viora never sees passwords/cookies.
public sealed class SavedRobloxAccount : System.ComponentModel.INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Username { get; set; } = "";
    public string Browser { get; set; } = "Edge";
    public string ProfileDirectory { get; set; } = "Default";
    public long? PlaceId { get; set; }
    public bool ManagedBrowserProfile { get; set; }
    // In-app WebView2 session is separate from old Edge/Chrome profiles.
    public bool EmbeddedBrowserProfile { get; set; }
    // Browser session user identity checked using Roblox authenticated-user API.
    // Not Roblox OAuth, nor proof of the active desktop Player identity.
    public long? SessionIdentityUserId { get; set; }
    public DateTime? SessionIdentityCheckedAtUtc { get; set; }

    // Local, optional organisation fields. Username is the legacy label,
    // not evidence of an authenticated Roblox browser session.
    public string Alias { get; set; } = "";
    public string RobloxUsername { get; set; } = "";
    public string Group { get; set; } = "";
    public string Tags { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsFavorite { get; set; }
    public bool IsPinned { get; set; }
    public long? PublicUserId { get; set; }
    public string PublicDisplayName { get; set; } = "";
    // Non-secret identity record from explicit, validated Roblox OAuth consent.
    // Does not prove that a desktop Roblox Player uses this same account.
    public long? VerifiedRobloxUserId { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }

    [JsonIgnore]
    public System.Windows.Media.ImageSource? AvatarImage { get; private set; }

    [JsonIgnore]
    public string DisplayLabel => !string.IsNullOrWhiteSpace(Alias) ? Alias :
        !string.IsNullOrWhiteSpace(PublicDisplayName) ? PublicDisplayName : Username;

    [JsonIgnore]
    public string AvatarInitial => DisplayLabel.Length == 0 ? "?" :
        DisplayLabel[..1].ToUpperInvariant();

    [JsonIgnore]
    public string SecondaryLine => PublicUserId is long id && id > 0
        ? "@" + (string.IsNullOrWhiteSpace(RobloxUsername) ? Username : RobloxUsername) +
          "  ·  #" + id + (EmbeddedBrowserProfile && SessionIdentityUserId == id
             ? "  ·  ✓ Browser login" : VerifiedRobloxUserId == id
             ? "  ·  ✓ OAuth linked" : "  ·  public lookup")
        : Username + "  ·  Roblox identity not linked";

    [JsonIgnore]
    public string Detail => (IsPinned ? "📌 " : "") +
        (IsFavorite ? "★ " : "") + Browser + " · " +
        (EmbeddedBrowserProfile ? "Embedded Roblox browser" :
         ManagedBrowserProfile ? "Private browser profile" : ProfileDirectory) +
        (PlaceId is long id ? " · Game " + id : " · No saved game") +
        (string.IsNullOrWhiteSpace(Group) ? "" : " · " + Group) +
        (string.IsNullOrWhiteSpace(Tags) ? "" : " · " + Tags);

    [JsonIgnore]
    public string SearchableText => string.Join(" ", new[]
    {
        Username, Alias, RobloxUsername, PublicDisplayName, Group, Tags, Notes,
        PublicUserId?.ToString() ?? "", VerifiedRobloxUserId?.ToString() ?? "",
        SessionIdentityUserId?.ToString() ?? ""
    });

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    internal void ApplyPublicProfile(RobloxPublicProfiles.PublicProfile profile)
    {
        PublicUserId = profile.UserId;
        PublicDisplayName = profile.DisplayName;
        AvatarImage = profile.Headshot;
        NotifyVisualUpdate();
    }

    public void NotifyVisualUpdate()
    {
        PropertyChanged?.Invoke(this,
            new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplayLabel)));
        PropertyChanged?.Invoke(this,
            new System.ComponentModel.PropertyChangedEventArgs(nameof(SecondaryLine)));
        PropertyChanged?.Invoke(this,
            new System.ComponentModel.PropertyChangedEventArgs(nameof(AvatarInitial)));
        PropertyChanged?.Invoke(this,
            new System.ComponentModel.PropertyChangedEventArgs(nameof(AvatarImage)));
        PropertyChanged?.Invoke(this,
            new System.ComponentModel.PropertyChangedEventArgs(nameof(Detail)));
        PropertyChanged?.Invoke(this,
            new System.ComponentModel.PropertyChangedEventArgs(nameof(SearchableText)));
    }
}

public partial class AccountsWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Regex UsernamePattern = new(@"^[A-Za-z0-9_]{3,20}$", RegexOptions.CultureInvariant);
    private static readonly Regex ProfilePattern = new(@"^(Default|Profile [1-9][0-9]{0,3})$", RegexOptions.CultureInvariant);

    private readonly ObservableCollection<SavedRobloxAccount> _accounts = new();
    private string? _selectedId;
    private bool _readOnly;
    private bool _oauthBusy;
    private readonly string _storagePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Viora", "Manager", "accounts.json");

    public AccountsWindow(bool openLoginOnLaunch = false, string? selectAccountId = null)
    {
        InitializeComponent();
        for (int i = 0; i <= 25; i++)
            ProfileInput.Items.Add(i == 0 ? "Default" : "Profile " + i);
        ProfileInput.SelectedIndex = 0;
        AccountList.ItemsSource = _accounts;
        OAuthClientIdInput.Text = RobloxOAuthLinker.GetClientId();
        LoadShortcuts();
        RefreshEmptyState();
        if (selectAccountId is not null)
        {
            Loaded += (_, _) =>
                AccountList.SelectedItem = _accounts.FirstOrDefault(a => a.Id == selectAccountId);
        }
        if (openLoginOnLaunch)
            Loaded += (_, _) => BeginBrowserLogin();
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
            VerifyOAuthButton.IsEnabled = false;
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
        (a.ManagedBrowserProfile || a.EmbeddedBrowserProfile || ProfilePattern.IsMatch(a.ProfileDirectory)) &&
        (a.PlaceId is null or > 0);

    private void StartBrowserLogin_Click(object sender, RoutedEventArgs e) =>
        BeginBrowserLogin();

    private void BeginBrowserLogin()
    {
        if (_readOnly) return;
        string id = Guid.NewGuid().ToString("N");
        try
        {
            var browser = new RobloxEmbeddedLoginWindow(id) { Owner = this };
            if (browser.ShowDialog() != true || browser.DetectedUser is null)
            {
                LoginStatusText.Text = "Login wasn't completed. No new account was saved.";
                return;
            }
            var saved = EmbeddedRobloxAccountStore.SaveDetectedAccount(id, browser.DetectedUser);
            _accounts.Clear();
            LoadShortcuts();
            AccountList.SelectedItem = _accounts.FirstOrDefault(a => a.Id == saved.Id);
            LoginStatusText.Text = "Connected @" + saved.RobloxUsername +
                " · Roblox User ID " + saved.PublicUserId + ". The browser saved its session.";
            SaveMessage.Text = "Account connected automatically. You can add a custom nickname below.";
            RefreshEmptyState();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
            IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            MessageBox.Show(this, ex.Message, "Connect Roblox account",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveOAuthClient_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RobloxOAuthLinker.SaveClientId(OAuthClientIdInput.Text);
            OAuthIdentityStatus.Text = "OAuth Client ID configured. Now select an account and click Connect verified Roblox account.";
            LoginStatusText.Text = "Official Roblox OAuth is configured; new accounts can verify their identity automatically.";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Roblox OAuth setup",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenOAuthRegistration_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var browser = Process.Start(new ProcessStartInfo(RobloxOAuthLinker.RegistrationUrl)
                { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "Roblox Creator Dashboard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void VerifyOAuth_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly || _oauthBusy) return;
        if (AccountList.SelectedItem is not SavedRobloxAccount target)
        {
            MessageBox.Show(this, "Select a saved account first, or create one with Add browser account.",
                "Link Roblox account", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await VerifySavedAccountAsync(target);
    }

    private async System.Threading.Tasks.Task VerifySavedAccountAsync(SavedRobloxAccount target)
    {
        if (_readOnly || _oauthBusy) return;
        if (RobloxOAuthLinker.GetClientId().Length == 0)
        {
            OAuthSetupExpander.IsExpanded = true;
            LoginStatusText.Text = "Register a Roblox OAuth application with openid/profile scopes, " +
                "save its Client ID below, then click Connect verified Roblox account.";
            return;
        }
        _oauthBusy = true;
        VerifyOAuthButton.IsEnabled = false;
        StartBrowserLoginButton.IsEnabled = false;
        OAuthIdentityStatus.Text = "Authorise Viora in the Roblox browser window; " +
            "Viora is waiting for an official callback (up to four minutes).";
        try
        {
            var verified = await RobloxOAuthLinker.VerifyAsync(target);
            if (_accounts.Any(a => a.Id != target.Id &&
                (a.VerifiedRobloxUserId == verified.Id ||
                 string.Equals(a.RobloxUsername, verified.Username,
                     StringComparison.OrdinalIgnoreCase) && a.VerifiedRobloxUserId is > 0)))
                throw new InvalidOperationException(
                    "This Roblox identity is already linked to another account shortcut.");

            var old = new
            {
                target.Username, target.RobloxUsername, target.PublicDisplayName,
                target.PublicUserId, target.VerifiedRobloxUserId, target.VerifiedAtUtc
            };
            target.RobloxUsername = verified.Username;
            target.PublicUserId = verified.Id;
            target.PublicDisplayName = verified.DisplayName;
            target.VerifiedRobloxUserId = verified.Id;
            target.VerifiedAtUtc = DateTime.UtcNow;

            // Replace an autogenerated AccountN label automatically but preserve
            // a user-chosen custom name or an existing same-name shortcut.
            if (Regex.IsMatch(target.Username, @"^Account\d+$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
                !_accounts.Any(a => a.Id != target.Id &&
                    a.Username.Equals(verified.Username, StringComparison.OrdinalIgnoreCase)))
                target.Username = verified.Username;

            if (!Persist())
            {
                target.Username = old.Username;
                target.RobloxUsername = old.RobloxUsername;
                target.PublicDisplayName = old.PublicDisplayName;
                target.PublicUserId = old.PublicUserId;
                target.VerifiedRobloxUserId = old.VerifiedRobloxUserId;
                target.VerifiedAtUtc = old.VerifiedAtUtc;
                throw new IOException("Could not save the verified identity; original shortcut preserved.");
            }
            target.NotifyVisualUpdate();
            AccountList.SelectedItem = null;
            AccountList.SelectedItem = target;
            OAuthIdentityStatus.Text = "✓ Verified @" + verified.Username +
                " (Roblox User ID " + verified.Id + ").";
            LoginStatusText.Text = "Roblox confirmed " + verified.Username +
                ". Viora saved the verified User ID and public display name, never the OAuth tokens.";
            SaveMessage.Text = "The browser is still responsible for its sign-in. " +
                "Verified identity does not control Roblox Player's active account.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or
            HttpRequestException or System.Threading.Tasks.TaskCanceledException or
            JsonException or IOException or ArgumentException)
        {
            OAuthIdentityStatus.Text = "Roblox identity verification was not completed.";
            LoginStatusText.Text = "Existing saved shortcut remains available. " +
                "Confirm your Client ID, registered callback URL, browser login and Roblox consent.";
            MessageBox.Show(this, ex.Message, "Roblox OAuth verification",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _oauthBusy = false;
            VerifyOAuthButton.IsEnabled = !_readOnly;
            StartBrowserLoginButton.IsEnabled = !_readOnly;
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

            string browser = current?.ManagedBrowserProfile == true
                ? current.Browser
                : (BrowserInput.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Edge";
            string profile = current?.ManagedBrowserProfile == true
                ? "Default" : ProfileInput.Text.Trim();
            bool managed = current?.ManagedBrowserProfile ?? false;

            if (!UsernamePattern.IsMatch(username))
                throw new ArgumentException("Enter a username/label of 3–20 letters, numbers or underscores.");
            if (browser is not ("Edge" or "Chrome" or "Brave") ||
                (!managed && !ProfilePattern.IsMatch(profile)))
                throw new ArgumentException("Choose a supported browser and profile.");
            if (_accounts.Any(a => a.Id != _selectedId &&
                (a.Username.Equals(username, StringComparison.OrdinalIgnoreCase) ||
                 (!a.ManagedBrowserProfile && !managed &&
                  a.Browser == browser && a.ProfileDirectory == profile))))
                throw new ArgumentException("That username or legacy browser profile is already saved.");

            if (current is null && _accounts.Count >= 1000)
                throw new ArgumentException("The account shortcut limit of 1,000 has been reached.");
            string realUsername = RobloxUsernameInput.Text.Trim();
            string alias = AliasInput.Text.Trim();
            string group = GroupInput.Text.Trim();
            string tags = TagsInput.Text.Trim();
            string notes = NotesInput.Text.Trim();
            if (realUsername.Length != 0 && !UsernamePattern.IsMatch(realUsername))
                throw new ArgumentException("Real Roblox username must contain 3–20 letters, numbers or underscores.");

            bool samePublicProfile = current is not null &&
                string.Equals(current.RobloxUsername, realUsername, StringComparison.OrdinalIgnoreCase);
            var updated = new SavedRobloxAccount
            {
                Id = current?.Id ?? Guid.NewGuid().ToString("N"),
                Username = username,
                Browser = browser,
                ProfileDirectory = profile,
                PlaceId = place,
                ManagedBrowserProfile = managed,
                RobloxUsername = realUsername,
                Alias = alias,
                Group = group,
                Tags = tags,
                Notes = notes,
                IsFavorite = FavoriteInput.IsChecked == true,
                IsPinned = PinnedInput.IsChecked == true,
                PublicUserId = samePublicProfile ? current!.PublicUserId : null,
                PublicDisplayName = samePublicProfile ? current!.PublicDisplayName : "",
                VerifiedRobloxUserId = samePublicProfile ? current!.VerifiedRobloxUserId : null,
                VerifiedAtUtc = samePublicProfile ? current!.VerifiedAtUtc : null,
                EmbeddedBrowserProfile = current?.EmbeddedBrowserProfile ?? false,
                SessionIdentityUserId = samePublicProfile ? current!.SessionIdentityUserId : null,
                SessionIdentityCheckedAtUtc = samePublicProfile ? current!.SessionIdentityCheckedAtUtc : null
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

            ClearFields();
            SaveMessage.Text = current is null
                ? "Account shortcut saved. Use Open Roblox login to create a dedicated browser session."
                : "Saved changes. Roblox's browser login remains unchanged; the username is only a label.";
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
        _selectedId = account.Id;
        UsernameInput.Text = account.Username;
        PlaceInput.Text = account.PlaceId?.ToString() ?? "";
        RobloxUsernameInput.Text = account.RobloxUsername;
        AliasInput.Text = account.Alias;
        GroupInput.Text = account.Group;
        TagsInput.Text = account.Tags;
        NotesInput.Text = account.Notes;
        FavoriteInput.IsChecked = account.IsFavorite;
        PinnedInput.IsChecked = account.IsPinned;
        OptionalDetailsExpander.IsExpanded = !string.IsNullOrWhiteSpace(account.Alias) ||
            !string.IsNullOrWhiteSpace(account.RobloxUsername) ||
            !string.IsNullOrWhiteSpace(account.Group) || !string.IsNullOrWhiteSpace(account.Tags);
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
            ? "This shortcut uses an isolated browser login saved by " + account.Browser +
              ". You may rename the label; Viora does not verify Roblox identity."
            : "This is a legacy browser profile shortcut. You can still edit it.";
        SaveMessage.Text = "Editing " + account.Username +
            ". Roblox identity is controlled by the browser, not by this label.";
    }

    private void ClearFields()
    {
        _selectedId = null;
        AccountList.SelectedItem = null;
        UsernameInput.Clear();
        PlaceInput.Clear();
        RobloxUsernameInput.Clear();
        AliasInput.Clear();
        GroupInput.Clear();
        TagsInput.Clear();
        NotesInput.Clear();
        FavoriteInput.IsChecked = false;
        PinnedInput.IsChecked = false;
        OptionalDetailsExpander.IsExpanded = false;
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
            if (placeId is long place)
            {
                if (account.EmbeddedBrowserProfile)
                {
                    var game = new RobloxEmbeddedLoginWindow(account.Id,
                        RobloxBrowserLauncher.BuildGameStartUrl(place), detectLogin: false)
                    { Owner = this };
                    game.Show();
                }
                else
                {
                    RobloxBrowserLauncher.Open(account, place, tryJoinDirectly: true);
                }
                SaveMessage.Text = "Opened game for " + account.Username +
                    ". Check which account Roblox Player actually uses.";
            }
            else
            {
                var login = new RobloxEmbeddedLoginWindow(account.Id) { Owner = this };
                if (login.ShowDialog() == true && login.DetectedUser is not null)
                {
                    var linked = EmbeddedRobloxAccountStore.SaveDetectedAccount(account.Id,
                        login.DetectedUser);
                    _accounts.Clear();
                    LoadShortcuts();
                    AccountList.SelectedItem = _accounts.FirstOrDefault(x => x.Id == linked.Id);
                    RefreshEmptyState();
                    SaveMessage.Text = "Detected @" + linked.RobloxUsername +
                        " (Roblox User ID " + linked.PublicUserId +
                        "). Username and display name saved automatically.";
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception or
            FileNotFoundException or InvalidOperationException or IOException or
            UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(this, "Could not open Roblox: " + ex.Message,
                "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
