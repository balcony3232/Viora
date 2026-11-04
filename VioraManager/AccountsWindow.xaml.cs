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
public sealed class SavedRobloxAccount : System.ComponentModel.INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Username { get; set; } = "";
    public string Browser { get; set; } = "Edge";
    public string ProfileDirectory { get; set; } = "Default";
    public long? PlaceId { get; set; }
    public bool ManagedBrowserProfile { get; set; }

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
          "  ·  #" + id : Username + "  ·  public profile not linked";

    [JsonIgnore]
    public string Detail => (IsPinned ? "📌 " : "") +
        (IsFavorite ? "★ " : "") + Browser + " · " +
        (ManagedBrowserProfile ? "Private browser profile" : ProfileDirectory) +
        (PlaceId is long id ? " · Game " + id : " · No saved game") +
        (string.IsNullOrWhiteSpace(Group) ? "" : " · " + Group) +
        (string.IsNullOrWhiteSpace(Tags) ? "" : " · " + Tags);

    [JsonIgnore]
    public string SearchableText => string.Join(" ", new[]
    {
        Username, Alias, RobloxUsername, PublicDisplayName, Group, Tags, Notes,
        PublicUserId?.ToString() ?? ""
    });

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public void ApplyPublicProfile(RobloxPublicProfiles.PublicProfile profile)
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

        string browser = (BrowserInput.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Edge";
        int number = 1;
        while (_accounts.Any(a => a.Username.Equals(
                   "Account" + number, StringComparison.OrdinalIgnoreCase)))
        {
            number++;
        }

        // We cannot determine the user's authenticated Roblox account merely
        // by opening a browser, so save a generic, user-renamable label. Roblox
        // itself retains the login inside the browser-managed profile directory.
        var saved = new SavedRobloxAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = "Account" + number,
            Browser = browser,
            ProfileDirectory = "Default",
            ManagedBrowserProfile = true
        };

        // Persist the shortcut first. The user doesn't need to copy a name or
        // click a second Save button after completing Roblox's official login.
        _accounts.Add(saved);
        if (!Persist())
        {
            _accounts.Remove(saved);
            LoginStatusText.Text = "Couldn't save a browser profile. Roblox login wasn't opened.";
            return;
        }

        RefreshEmptyState();
        AccountList.SelectedItem = saved; // Show rename/game options, if wanted.
        LoginStatusText.Text = "Account slot saved as " + saved.Username +
            ". Opening Roblox login now. Sign in on the Roblox website; " +
            "the browser saves its own session automatically.";
        SaveMessage.Text = "Optional: rename " + saved.Username +
            " to the actual Roblox username and add a game ID below.";

        try
        {
            // Cookies/passwords remain managed by the browser, not by Viora.
            RobloxBrowserLauncher.OpenLogin(saved);
        }
        catch (Exception ex) when (ex is ArgumentException or
            System.ComponentModel.Win32Exception or
            FileNotFoundException or InvalidOperationException)
        {
            LoginStatusText.Text = "Saved the shortcut for " + saved.Username +
                ", but Roblox login couldn't be opened. Use the Sign in button below to retry.";
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
            var updated = new SavedRobloxAccount
            {
                Id = current?.Id ?? Guid.NewGuid().ToString("N"),
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
