using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VioraManager;

/// <summary>
/// Public Roblox user metadata only. Never sends or reads Roblox browser
/// cookies, passwords, sessions or authenticated account information.
/// Looking up an entered username does NOT verify who is logged in.
/// </summary>
internal static class RobloxPublicProfiles
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };
    private static readonly Regex RobloxName = new(
        "^[A-Za-z0-9_]{3,20}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public sealed record PublicProfile(long UserId, string Username, string DisplayName, ImageSource? Headshot);

    public static async Task<PublicProfile?> LookupAsync(string? input, CancellationToken cancel = default)
    {
        if (string.IsNullOrWhiteSpace(input) || !RobloxName.IsMatch(input)) return null;
        try
        {
            using var response = await Http.PostAsJsonAsync(
                "https://users.roblox.com/v1/usernames/users",
                new { usernames = new[] { input }, excludeBannedUsers = false }, cancel);
            if (!response.IsSuccessStatusCode) return null;
            using var result = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancel), cancellationToken: cancel);
            var root = result.RootElement;
            if (!root.TryGetProperty("data", out var users) ||
                users.ValueKind != JsonValueKind.Array || users.GetArrayLength() == 0) return null;
            var user = users[0];
            if (!user.TryGetProperty("id", out var idNode) || !idNode.TryGetInt64(out long id) || id < 1) return null;
            var name = user.GetProperty("name").GetString() ?? input;
            var display = user.TryGetProperty("displayName", out var displayNode)
                ? displayNode.GetString() ?? name : name;
            ImageSource? picture = await GetHeadshotAsync(id, cancel);
            return new PublicProfile(id, name, display, picture);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return null;
        }
    }

    private static async Task<ImageSource?> GetHeadshotAsync(long id, CancellationToken cancel)
    {
        try
        {
            string endpoint = "https://thumbnails.roblox.com/v1/users/avatar-headshot" +
                "?userIds=" + id + "&size=100x100&format=Png&isCircular=false";
            using var response = await Http.GetAsync(endpoint, cancel);
            if (!response.IsSuccessStatusCode) return null;
            using var result = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancel), cancellationToken: cancel);
            var data = result.RootElement.GetProperty("data");
            if (data.GetArrayLength() == 0) return null;
            var row = data[0];
            if (!row.TryGetProperty("imageUrl", out var imageNode)) return null;
            string? imageUrl = imageNode.GetString();
            if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !(uri.Host.Equals("rbxcdn.com", StringComparison.OrdinalIgnoreCase) ||
                  uri.Host.EndsWith(".rbxcdn.com", StringComparison.OrdinalIgnoreCase)))
                return null;

            using var imageResponse = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!imageResponse.IsSuccessStatusCode ||
                imageResponse.Content.Headers.ContentLength > 512 * 1024) return null;
            var bytes = await imageResponse.Content.ReadAsByteArrayAsync(cancel);
            if (bytes.Length > 512 * 1024) return null;
            using var memory = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = memory;
            bitmap.DecodePixelWidth = 100;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze(); // Cross-thread safe for WPF-bound data.
            return bitmap;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or
            JsonException or IOException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
}
