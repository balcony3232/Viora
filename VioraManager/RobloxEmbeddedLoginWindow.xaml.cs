using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace VioraManager;

/// <summary>
/// Interactive, browser-owned Roblox session. The app reads only the JSON
/// identity from Roblox's authenticated-user API, not cookies/passwords.
/// Every account gets a separate, persistent WebView2 user-data folder.
/// </summary>
public partial class RobloxEmbeddedLoginWindow : Window
{
    public sealed record DetectedRobloxUser(long UserId, string Username, string DisplayName);

    private const string LoginUrl = "https://www.roblox.com/login";
    private const string HomeUrl = "https://www.roblox.com/home";
    private const string IdentityUrl = "https://users.roblox.com/v1/users/authenticated";

    private readonly string _accountId;
    private readonly string _startingUrl;
    private readonly bool _detectLogin;
    private bool _initialised;
    private bool _checking;
    private bool _finished;
    private DateTime _lastCheck = DateTime.MinValue;

    public DetectedRobloxUser? DetectedUser { get; private set; }

    public RobloxEmbeddedLoginWindow(string accountId, string? startUrl = null,
        bool detectLogin = true)
    {
        if (!Guid.TryParseExact(accountId, "N", out _))
            throw new ArgumentException("Invalid saved account profile ID.", nameof(accountId));

        _accountId = accountId;
        _startingUrl = startUrl ?? LoginUrl;
        _detectLogin = detectLogin;
        InitializeComponent();
        if (!IsApprovedStartingUrl(_startingUrl))
            throw new ArgumentException("Only official Roblox pages may be opened.");
        if (!_detectLogin)
        {
            Title = "Viora Manager — Roblox Game";
            StatusText.Text = "Opening the selected Roblox game in this account's saved in-app browser. " +
                "The Roblox desktop Player may use a different signed-in account.";
            CheckButton.Visibility = Visibility.Collapsed;
        }
        Loaded += async (_, _) => await InitialiseAsync();
        Closed += (_, _) => RobloxWebView.Dispose();
    }

    private static bool IsApprovedStartingUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
        parsed.Scheme == Uri.UriSchemeHttps &&
        (parsed.Host.Equals("www.roblox.com", StringComparison.OrdinalIgnoreCase) ||
         parsed.Host.Equals("roblox.com", StringComparison.OrdinalIgnoreCase)) &&
        (parsed.AbsolutePath.StartsWith("/games/", StringComparison.OrdinalIgnoreCase) ||
         parsed.AbsolutePath.Equals("/home", StringComparison.OrdinalIgnoreCase) ||
         parsed.AbsolutePath.Equals("/login", StringComparison.OrdinalIgnoreCase));

    private async Task InitialiseAsync()
    {
        if (_initialised) return;
        try
        {
            // Separate from the old Edge/Chrome/Brave BrowserProfiles directory.
            // WebView2 owns its cookies and credentials, not the Manager.
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Viora", "Manager", "WebViewProfiles", _accountId);
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null, userDataFolder: userDataFolder);
            await RobloxWebView.EnsureCoreWebView2Async(environment);
            RobloxWebView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            RobloxWebView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            RobloxWebView.CoreWebView2.NavigationCompleted += NavigationCompleted;
            _initialised = true;
            RobloxWebView.CoreWebView2.Navigate(_startingUrl);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Embedded browser couldn't start: " + ex.Message +
                ". Microsoft Edge WebView2 Runtime must be installed.";
            CheckButton.IsEnabled = false;
        }
    }

    private async void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!_detectLogin || _finished || !_initialised || !args.IsSuccess) return;
        if (!Uri.TryCreate(RobloxWebView.CoreWebView2.Source, UriKind.Absolute, out var url))
            return;

        if (url.Host.Equals("users.roblox.com", StringComparison.OrdinalIgnoreCase) &&
            url.AbsolutePath.Equals("/v1/users/authenticated", StringComparison.OrdinalIgnoreCase))
        {
            await ReadAuthenticationResponseAsync();
            return;
        }

        // After the user signs in, Roblox normally navigates to /home.
        // We automatically probe then; the button remains available if
        // Roblox uses an SPA route and does not trigger a full navigation.
        if ((url.Host.Equals("www.roblox.com", StringComparison.OrdinalIgnoreCase) ||
             url.Host.Equals("roblox.com", StringComparison.OrdinalIgnoreCase)) &&
            url.AbsolutePath.Equals("/home", StringComparison.OrdinalIgnoreCase))
        {
            CheckAuthenticated();
        }
    }

    private void CheckAuthenticated()
    {
        if (!_initialised || !_detectLogin || _checking || _finished) return;
        if (DateTime.UtcNow - _lastCheck < TimeSpan.FromSeconds(2)) return;
        _lastCheck = DateTime.UtcNow;
        _checking = true;
        CheckButton.IsEnabled = false;
        StatusText.Text = "Checking your Roblox login… Only account identity is read.";
        // Navigation is performed by the SAME WebView2 profile, so browser
        // cookies are sent to Roblox by WebView2 automatically. Viora does
        // not inspect, read, or transfer them.
        RobloxWebView.CoreWebView2.Navigate(IdentityUrl);
    }

    private async Task ReadAuthenticationResponseAsync()
    {
        try
        {
            // Only parse the rendered text of an exact, official Roblox
            // authenticated-user endpoint; never evaluate login-page scripts
            // for passwords or cookies.
            string scriptResult = await RobloxWebView.ExecuteScriptAsync(
                "document.body ? document.body.innerText : ''");
            string text = JsonSerializer.Deserialize<string>(scriptResult) ?? "";
            using var data = JsonDocument.Parse(text);
            var root = data.RootElement;
            if (!root.TryGetProperty("id", out var idElement) ||
                !idElement.TryGetInt64(out long id) || id <= 0 ||
                !root.TryGetProperty("name", out var usernameElement))
                throw new InvalidDataException("Roblox hasn't returned an authenticated account yet.");
            string username = usernameElement.GetString() ?? "";
            if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[A-Za-z0-9_]{3,20}$"))
                throw new InvalidDataException("Roblox returned an invalid username.");

            string displayName = root.TryGetProperty("displayName", out var displayElement)
                ? displayElement.GetString() ?? username : username;

            DetectedUser = new DetectedRobloxUser(id, username, displayName);
            _finished = true;
            StatusText.Text = "Connected @" + username + " · Roblox User ID " + id;
            // Modal Add Account windows return the identity to the local
            // shortcut manager; access tokens or cookies are never returned.
            DialogResult = true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or
            InvalidOperationException or ArgumentException)
        {
            StatusText.Text = "No signed-in Roblox account detected. Complete login and press " +
                "'Detect signed-in account' if it doesn't update automatically.";
            RobloxWebView.CoreWebView2.Navigate(LoginUrl);
        }
        finally
        {
            _checking = false;
            CheckButton.IsEnabled = true;
        }
    }

    private void Check_Click(object sender, RoutedEventArgs e) => CheckAuthenticated();

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialised) return;
        RobloxWebView.CoreWebView2.Navigate(HomeUrl);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
