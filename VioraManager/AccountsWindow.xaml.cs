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

    [JsonIgnore]
    public string Detail => Browser + " · " + ProfileDirectory +
        (PlaceId is long id ? " · Game " + id : " · No saved game");
}

public partial class AccountsWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Regex UsernamePattern = new(@"^[A-Za-z0-9_]{3,20}$", RegexOptions.CultureInvariant);
    private static readonly Regex ProfilePattern = new(@"^(Default|Profile ([1-9]|1[0-5]))$", RegexOptions.CultureInvariant);

    private readonly ObservableCollection<SavedRobloxAccount> _accounts = new();
    private string? _selectedId;
    private bool _readOnly;
    private readonly string _storagePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Viora", "Manager", "accounts.json");

    public AccountsWindow()
    {
        InitializeComponent();
        for (int i = 0; i <= 15; i++)
            ProfileInput.Items.Add(i == 0 ? "Default" : "Profile " + i);
        ProfileInput.SelectedIndex = 0;
        AccountList.ItemsSource = _accounts;
        LoadShortcuts();
        RefreshEmptyState();
    }

    private void LoadShortcuts()
    {
        if (!File.Exists(_storagePath)) return;
        try
        {
            var json = File.ReadAllText(_storagePath);
            var entries = JsonSerializer.Deserialize<List<SavedRobloxAccount>>(json, JsonOptions);
            if (entries is null || entries.Count > 100) throw new InvalidDataException("Invalid account file.");
            var seenUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (!IsValid(entry) || !seenUsers.Add(entry.Username) ||
                    !seenIds.Add(entry.Id) ||
                    !seenProfiles.Add(entry.Browser + ":" + entry.ProfileDirectory))
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
        a.Browser is "Edge" or "Chrome" &&
        a.ProfileDirectory is not null && ProfilePattern.IsMatch(a.ProfileDirectory) &&
        (a.PlaceId is null or > 0);

    private static long? ParsePlaceId(string value)
    {
        var input = value.Trim();
        if (input.Length == 0) return null;
        if (long.TryParse(input, out long numeric) && numeric > 0) return numeric;
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            (uri.Host.Equals("www.roblox.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.Equals("roblox.com", StringComparison.OrdinalIgnoreCase)))
        {
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length >= 2 &&
                parts[0].Equals("games", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(parts[1], out long game) && game > 0)
                return game;
        }
        throw new ArgumentException("Use a positive numeric Roblox place ID or official https://www.roblox.com/games/... URL.");
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
            string browser = (BrowserInput.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Edge";
            string profile = ProfileInput.SelectedItem as string ?? "Default";
            long? place = ParsePlaceId(PlaceInput.Text);
            if (!UsernamePattern.IsMatch(username))
                throw new ArgumentException("Use a Roblox username with 3–20 letters, numbers or underscores.");
            if (browser is not ("Edge" or "Chrome") || !ProfilePattern.IsMatch(profile))
                throw new ArgumentException("Choose a supported browser and profile.");

            // Each saved account must reference a DIFFERENT browser profile.
            // Reusing a single browser session would silently use the wrong Roblox login.
            if (_accounts.Any(a => a.Id != _selectedId &&
                (a.Username.Equals(username, StringComparison.OrdinalIgnoreCase) ||
                 (a.Browser == browser && a.ProfileDirectory == profile))))
                throw new ArgumentException("That username or browser profile is already saved. Use a separate browser profile for each Roblox account.");

            var current = _accounts.FirstOrDefault(x => x.Id == _selectedId);
            bool editing = current is not null;
            var updated = new SavedRobloxAccount
            {
                Id = current?.Id ?? Guid.NewGuid().ToString("N"),
                Username = username,
                Browser = browser,
                ProfileDirectory = profile,
                PlaceId = place
            };
            int index = current is null ? -1 : _accounts.IndexOf(current);
            if (index >= 0) _accounts[index] = updated;
            else _accounts.Add(updated);
            if (!Persist())
            {
                // Keep in-memory data consistent with the unchanged on-disk copy.
                if (index >= 0 && current is not null) _accounts[index] = current;
                else _accounts.Remove(updated);
                return;
            }
            ClearFields();
            SaveMessage.Text = editing ? "Shortcut updated." : "Account shortcut added. Sign into Roblox via its browser profile.";
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
        BrowserInput.SelectedIndex = account.Browser == "Chrome" ? 1 : 0;
        ProfileInput.SelectedItem = account.ProfileDirectory;
        SaveButton.Content = "Save changes";
        SaveMessage.Text = "Editing " + account.Username + ". Browser profiles are not auto-authenticated.";
    }

    private void ClearFields()
    {
        _selectedId = null;
        AccountList.SelectedItem = null;
        UsernameInput.Clear();
        PlaceInput.Clear();
        BrowserInput.SelectedIndex = 0;
        ProfileInput.SelectedIndex = 0;
        SaveButton.Content = "Add account";
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
            "Remove the shortcut for " + account.Username + "?\n\nThis does NOT delete or log out of the Roblox account.",
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
            string executable = FindInstalledBrowser(account.Browser) ??
                throw new FileNotFoundException(account.Browser + " is not installed in a standard location.");
            // Only official HTTPS Roblox pages are opened. An isolated browser
            // profile handles login; never consume a cookie, password or auth token.
            string url = placeId is long id
                ? "https://www.roblox.com/games/" + id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "https://www.roblox.com/home";
            var info = new ProcessStartInfo(executable) { UseShellExecute = false };
            info.ArgumentList.Add("--profile-directory=" + account.ProfileDirectory);
            info.ArgumentList.Add("--new-window");
            info.ArgumentList.Add(url);
            using var started = Process.Start(info);
            SaveMessage.Text = "Opened " + account.Browser + " (" + account.ProfileDirectory + ") for " +
                account.Username + ". Check the Roblox username in the browser before pressing Play.";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or
            FileNotFoundException or InvalidOperationException)
        {
            MessageBox.Show(this, "Could not open the selected browser profile: " + ex.Message,
                "Open official Roblox website", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string? FindInstalledBrowser(string browser)
    {
        string name = browser == "Edge" ? "msedge.exe" : "chrome.exe";
        string vendor = browser == "Edge" ? @"Microsoft\Edge\Application" : @"Google\Chrome\Application";
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        foreach (string root in roots.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var path = Path.Combine(root, vendor, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
