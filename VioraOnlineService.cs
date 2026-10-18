using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Viora;

public sealed record VioraOnlineProfile(long PublicId, string Username, string Role,
    long PlaytimeSeconds, bool LeaderboardOptIn);
public sealed record VioraLeaderboardEntry(long Rank, long PublicId, string Username,
    string Role, long PlaytimeSeconds);

// Desktop clients may use only a Supabase publishable key. Roles and scores are
// controlled by database RPCs, never by local files or client-provided totals.
public sealed class VioraOnlineService : IDisposable
{
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private string? _accessToken;
    private string? _refreshToken;
    private DateTimeOffset _expiresAt;
    public string SuggestedUsername { get; private set; } = "";
    public bool IsSignedIn => !string.IsNullOrEmpty(_accessToken);

    private VioraOnlineService(string url, string publicKey)
    {
        _client = new HttpClient
        {
            BaseAddress = new Uri(url.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(20)
        };
        _client.DefaultRequestHeaders.Add("apikey", publicKey);
    }

    public static VioraOnlineService? FromEmbeddedConfig()
    {
        using var stream = typeof(VioraOnlineService).Assembly
            .GetManifestResourceStream("Viora.Assets.OnlineConfig.json");
        if (stream is null) return null;
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        string url = root.TryGetProperty("supabaseUrl", out var node) ? node.GetString() ?? "" : "";
        string key = root.TryGetProperty("publishableKey", out node) ? node.GetString() ?? "" : "";
        // Do not accept self-hosted/HTTP URLs or private Supabase API keys.
        if (!Regex.IsMatch(url, @"^https://[a-z0-9-]+\.supabase\.co/?$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase) ||
            !Regex.IsMatch(key, @"^sb_publishable_[A-Za-z0-9_-]{10,}$",
                RegexOptions.CultureInvariant))
            return null;
        return new VioraOnlineService(url, key);
    }

    private static StringContent AsJson(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private async Task<JsonDocument> SendAsync(HttpMethod method, string route,
        object? body = null, string? bearer = null)
    {
        using var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = AsJson(body);
        if (bearer is not null)
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        using var response = await _client.SendAsync(request);
        string raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            string message = response.StatusCode switch
            {
                HttpStatusCode.Conflict => "That username is already taken. Choose another.",
                HttpStatusCode.Unauthorized => "Sign in again to continue.",
                HttpStatusCode.Forbidden => "Access denied. Check email verification and account permissions.",
                HttpStatusCode.TooManyRequests => "Too many requests. Please wait and retry.",
                HttpStatusCode.NotFound => "Online account database has not been configured yet.",
                _ => "Online request failed (" + (int)response.StatusCode +
                    "). Check your credentials, connection or email verification."
            };
            throw new InvalidOperationException(message);
        }
        return JsonDocument.Parse(raw);
    }

    public static void ValidateUsername(string username)
    {
        if (!Regex.IsMatch(username, @"^[A-Za-z0-9_]{3,24}$"))
            throw new ArgumentException("Usernames must be 3–24 letters, numbers or underscores.");
    }

    public async Task RegisterAsync(string username, string email, string password)
    {
        username = username.Trim();
        email = email.Trim();
        ValidateUsername(username);
        if (email.Length > 254 || !email.Contains('@'))
            throw new ArgumentException("Enter a valid email address.");
        if (password.Length < 10 || password.Length > 128)
            throw new ArgumentException("Choose a password between 10 and 128 characters.");
        // Does not reserve the requested name until the email is verified,
        // sign-in succeeds, and the user calls viora_claim_username.
        using var result = await SendAsync(HttpMethod.Post, "auth/v1/signup",
            new { email, password, data = new { username } });
    }

    public async Task<VioraOnlineProfile?> SignInAsync(string email, string password)
    {
        using var response = await SendAsync(HttpMethod.Post,
            "auth/v1/token?grant_type=password",
            new { email = email.Trim(), password });
        SetSession(response.RootElement);
        SuggestedUsername = "";
        if (response.RootElement.TryGetProperty("user", out var user) &&
            user.TryGetProperty("user_metadata", out var metadata) &&
            metadata.TryGetProperty("username", out var suggested))
            SuggestedUsername = suggested.GetString() ?? "";
        try { return await MyProfileAsync(); }
        catch { ForgetSession(); throw; }
    }

    private void SetSession(JsonElement root)
    {
        _accessToken = root.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Missing access token.");
        _refreshToken = root.GetProperty("refresh_token").GetString()
            ?? throw new InvalidOperationException("Missing refresh token.");
        int duration = root.TryGetProperty("expires_in", out var field) &&
            field.TryGetInt32(out int seconds) ? seconds : 3600;
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, duration));
    }

    private async Task<string> TokenAsync()
    {
        if (_accessToken is null)
            throw new InvalidOperationException("Sign in first.");
        if (_expiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
        await _refreshGate.WaitAsync();
        try
        {
            if (_accessToken is null)
                throw new InvalidOperationException("Sign in first.");
            if (_expiresAt <= DateTimeOffset.UtcNow.AddMinutes(1))
            {
                if (_refreshToken is null)
                    throw new InvalidOperationException("Your session expired. Sign in again.");
                using var response = await SendAsync(HttpMethod.Post,
                    "auth/v1/token?grant_type=refresh_token",
                    new { refresh_token = _refreshToken });
                SetSession(response.RootElement);
            }
            return _accessToken;
        }
        finally { _refreshGate.Release(); }
    }

    private async Task<JsonDocument> RpcAsync(string name, object args)
        => await SendAsync(HttpMethod.Post, "rest/v1/rpc/" + name, args, await TokenAsync());

    private static VioraOnlineProfile ReadProfile(JsonElement element) => new(
        element.GetProperty("public_id").GetInt64(),
        element.GetProperty("username").GetString() ?? "",
        element.GetProperty("role").GetString() ?? "member",
        element.GetProperty("playtime_seconds").GetInt64(),
        element.GetProperty("leaderboard_opt_in").GetBoolean());

    public async Task<VioraOnlineProfile?> MyProfileAsync()
    {
        using var data = await RpcAsync("viora_me", new { });
        return data.RootElement.ValueKind == JsonValueKind.Array &&
               data.RootElement.GetArrayLength() > 0
            ? ReadProfile(data.RootElement[0]) : null;
    }

    public async Task<VioraOnlineProfile> ClaimUsernameAsync(string username)
    {
        ValidateUsername(username.Trim());
        using var data = await RpcAsync("viora_claim_username",
            new { p_username = username.Trim() });
        if (data.RootElement.ValueKind != JsonValueKind.Array ||
            data.RootElement.GetArrayLength() == 0)
            throw new InvalidOperationException("Could not create online profile.");
        return ReadProfile(data.RootElement[0]);
    }

    public async Task<IReadOnlyList<VioraLeaderboardEntry>> LeaderboardAsync()
    {
        using var data = await RpcAsync("viora_leaderboard", new { p_limit = 50 });
        var rows = new List<VioraLeaderboardEntry>();
        foreach (var row in data.RootElement.EnumerateArray())
            rows.Add(new VioraLeaderboardEntry(row.GetProperty("rank").GetInt64(),
                row.GetProperty("public_id").GetInt64(),
                row.GetProperty("username").GetString() ?? "",
                row.GetProperty("role").GetString() ?? "member",
                row.GetProperty("playtime_seconds").GetInt64()));
        return rows;
    }

    public async Task<VioraOnlineProfile?> SetLeaderboardVisibilityAsync(bool enabled)
    {
        using var result = await RpcAsync("viora_set_leaderboard_visibility",
            new { p_visible = enabled });
        return await MyProfileAsync();
    }

    public async Task<long> PingAsync(bool playing)
    {
        using var result = await RpcAsync("viora_playtime_ping",
            new { p_playing = playing });
        return result.RootElement.GetInt64();
    }

    public async Task SignOutAsync()
    {
        var token = _accessToken;
        ForgetSession();
        if (token is null) return;
        try
        {
            using var response = await SendAsync(HttpMethod.Post, "auth/v1/logout",
                null, token);
        }
        catch (HttpRequestException) { /* Local sign-out takes effect regardless. */ }
        catch (InvalidOperationException) { /* Remote session may already have expired. */ }
    }

    public void ForgetSession()
    {
        _accessToken = null;
        _refreshToken = null;
        _expiresAt = DateTimeOffset.MinValue;
        SuggestedUsername = "";
    }

    public void Dispose()
    {
        ForgetSession();
        _client.Dispose();
        _refreshGate.Dispose();
    }
}
