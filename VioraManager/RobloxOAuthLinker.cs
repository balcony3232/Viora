using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace VioraManager;

/// <summary>
/// Official Roblox public-client OAuth authorization-code + PKCE flow.
/// Requires the user to register a Roblox OAuth application with exact
/// localhost redirect URI. No client secrets, cookies, tokens or passwords
/// are written to disk or logged.
/// </summary>
internal static class RobloxOAuthLinker
{
    public const string RedirectUri = "http://localhost:39141/oauth/callback/";
    public const string RegistrationUrl = "https://create.roblox.com/dashboard/credentials";

    private static readonly string ClientIdFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Viora", "Manager", "roblox-oauth-client-id.txt");
    private static readonly Regex ClientIdPattern = new(@"^\d{8,40}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    internal sealed record VerifiedUser(long Id, string Username, string DisplayName);

    public static string GetClientId()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable("VIORA_ROBLOX_OAUTH_CLIENT_ID");
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && ClientIdPattern.IsMatch(fromEnvironment.Trim()))
            return fromEnvironment.Trim();
        try
        {
            if (File.Exists(ClientIdFile) && new FileInfo(ClientIdFile).Length < 128)
            {
                var stored = File.ReadAllText(ClientIdFile).Trim();
                if (ClientIdPattern.IsMatch(stored)) return stored;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return "";
    }

    public static void SaveClientId(string value)
    {
        value = value.Trim();
        if (!ClientIdPattern.IsMatch(value))
            throw new ArgumentException(
                "Enter your numeric Roblox OAuth application Client ID, NOT a client secret.");
        string directory = Path.GetDirectoryName(ClientIdFile)!;
        Directory.CreateDirectory(directory);
        string temporary = ClientIdFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, value + Environment.NewLine);
            File.Move(temporary, ClientIdFile, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
        }
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static async Task<VerifiedUser> VerifyAsync(SavedRobloxAccount account)
    {
        string clientId = GetClientId();
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException(
                "A Roblox OAuth Client ID is required. Create an OAuth application " +
                "with 'openid' and 'profile' scopes, register " + RedirectUri +
                " as its exact Redirect URL, then save its Client ID in Viora. Never enter a client secret.");

        // Fix the local callback port, so the user only registers one redirect.
        using var listener = new HttpListener();
        listener.Prefixes.Add(RedirectUri);
        try { listener.Start(); }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException("Couldn't start the local OAuth callback at " +
                RedirectUri + ". Another application may be using port 39141 or Windows may " +
                "require local listener permissions. " + ex.Message, ex);
        }

        string verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = Base64Url(RandomNumberGenerator.GetBytes(32));
        string nonce = Base64Url(RandomNumberGenerator.GetBytes(32));
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = RedirectUri,
            ["scope"] = "openid profile",
            ["state"] = state,
            ["nonce"] = nonce,
            ["prompt"] = "select_account",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        };
        string query = string.Join("&", parameters.Select(x =>
            Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
        string authorizationUrl = "https://apis.roblox.com/oauth/v1/authorize?" + query;

        // Open the authorization URL under the same isolated browser profile as
        // that account shortcut. Roblox controls the login and consent UI.
        RobloxBrowserLauncher.OpenOAuthAuthorization(account, authorizationUrl);

        using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(4));
        HttpListenerContext context;
        try { context = await listener.GetContextAsync().WaitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Roblox authorization wasn't completed within four minutes. " +
                "Try Connect Roblox again.");
        }
        // Only locally delivered callback data, never third-party link input.
        try
        {
            if (context.Request.HttpMethod != "GET" ||
                context.Request.Url is null ||
                context.Request.Url.AbsolutePath != "/oauth/callback/")
                throw new InvalidOperationException("Unexpected OAuth redirect request.");
            string returnedState = context.Request.QueryString["state"] ?? "";
            if (returnedState.Length != state.Length ||
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(returnedState), Encoding.ASCII.GetBytes(state)))
                throw new InvalidOperationException("OAuth state mismatch: link was not accepted.");
            string? error = context.Request.QueryString["error"];
            if (!string.IsNullOrEmpty(error))
                throw new InvalidOperationException("Roblox authorization was cancelled or refused: " + error);
            string code = context.Request.QueryString["code"] ?? "";
            if (code.Length < 8 || code.Length > 4096)
                throw new InvalidOperationException("Roblox didn't supply a valid authorization code.");

            byte[] responseBytes = Encoding.UTF8.GetBytes(
                "<!doctype html><meta charset='utf-8'><title>Return to Viora</title>" +
                "<body style='font-family:sans-serif;background:#120e1e;color:#dfcaff;padding:32px'>" +
                "<h2>Return to Viora Manager</h2>" +
                "<p>Roblox authorization was received. You can close this browser tab.</p></body>");
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes);
            context.Response.Close();

            using var requestBody = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["code_verifier"] = verifier,
                ["code"] = code,
                ["redirect_uri"] = RedirectUri
            });
            using var tokenResponse = await Http.PostAsync(
                "https://apis.roblox.com/oauth/v1/token", requestBody);
            if (!tokenResponse.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    "Roblox couldn't complete the OAuth token exchange (HTTP " +
                    (int)tokenResponse.StatusCode + "). Check the app's Client ID, " +
                    "exact redirect and PKCE settings.");

            using var tokenDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
            if (!tokenDoc.RootElement.TryGetProperty("access_token", out var tokenElement))
                throw new InvalidOperationException("Roblox returned no access token.");
            string accessToken = tokenElement.GetString() ?? "";
            if (accessToken.Length < 8)
                throw new InvalidOperationException("Roblox returned an invalid access token.");

            using var infoRequest = new HttpRequestMessage(HttpMethod.Get,
                "https://apis.roblox.com/oauth/v1/userinfo");
            infoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var infoResponse = await Http.SendAsync(infoRequest);
            if (!infoResponse.IsSuccessStatusCode)
                throw new InvalidOperationException("Roblox userinfo request failed (HTTP " +
                    (int)infoResponse.StatusCode + ").");
            using var infoDoc = JsonDocument.Parse(await infoResponse.Content.ReadAsStringAsync());
            var identity = infoDoc.RootElement;
            if (!long.TryParse(identity.GetProperty("sub").GetString(), out long userId) ||
                userId <= 0)
                throw new InvalidOperationException("Roblox didn't return a valid User ID.");
            string username = identity.GetProperty("preferred_username").GetString() ?? "";
            if (!Regex.IsMatch(username, @"^[A-Za-z0-9_]{3,20}$"))
                throw new InvalidOperationException("Roblox userinfo didn't return a username.");
            string displayName = identity.TryGetProperty("name", out var name)
                ? (name.GetString() ?? username) : username;

            // We deliberately don't persist the OAuth token or refresh token.
            // A later identity refresh requires a new explicit authorization.
            return new VerifiedUser(userId, username, displayName);
        }
        finally
        {
            try
            {
                if (context.Response.OutputStream.CanWrite)
                {
                    context.Response.StatusCode = 200;
                    context.Response.Close();
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException) { }
        }
    }
}
