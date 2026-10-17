using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Viora;

/// <summary>
/// Retrieves *public* Roblox place-icon thumbnails. No cookies, tokens or private APIs.
/// Downloads are size-limited and cached under LocalAppData for offline reuse.
/// </summary>
internal static class RobloxGameIcons
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(14);
    private const int MaxImageBytes = 900 * 1024;
    private static string CacheDirectory => Path.Combine(Storage.Root, "game-icons");
    private static string CachePath(long placeId) => Path.Combine(CacheDirectory, $"{placeId}.png");

    public static ImageSource? TryLoadCached(long placeId)
    {
        var path = CachePath(placeId);
        if (!File.Exists(path)) return null;
        try
        {
            // OnLoad closes the backing stream and lets the cache be refreshed safely.
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 150;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            // Corrupt/offline assets never block the activity hub.
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return null;
        }
    }

    public static async Task EnsureCachedAsync(IReadOnlyList<long> placeIds)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var missing = placeIds.Where(id => id > 0 && (!File.Exists(CachePath(id)) ||
                DateTime.UtcNow - File.GetLastWriteTimeUtc(CachePath(id)) > CacheLifetime)).Distinct().ToArray();
            // Roblox's documented public thumbnail endpoint accepts comma-separated place IDs.
            foreach (var batch in missing.Chunk(25))
            {
                var query = string.Join(",", batch);
                var url = $"https://thumbnails.roblox.com/v1/places/gameicons?placeIds={query}&returnPolicy=PlaceHolder&size=150x150&format=Png&isCircular=false";
                using var apiReply = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!apiReply.IsSuccessStatusCode) continue;
                await using var apiStream = await apiReply.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var response = await JsonDocument.ParseAsync(apiStream).ConfigureAwait(false);
                if (!response.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in data.EnumerateArray())
                {
                    if (!item.TryGetProperty("targetId", out var target) || !target.TryGetInt64(out var id) || !batch.Contains(id)) continue;
                    if (!item.TryGetProperty("state", out var state) || state.GetString() != "Completed") continue;
                    if (!item.TryGetProperty("imageUrl", out var imageUrl) || imageUrl.ValueKind != JsonValueKind.String) continue;
                    if (!Uri.TryCreate(imageUrl.GetString(), UriKind.Absolute, out var imageUri) ||
                        imageUri.Scheme != Uri.UriSchemeHttps ||
                        !imageUri.Host.EndsWith(".rbxcdn.com", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        using var imageReply = await Client.GetAsync(imageUri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                        if (!imageReply.IsSuccessStatusCode || imageReply.Content.Headers.ContentLength > MaxImageBytes) continue;
                        await using var imageStream = await imageReply.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        using var buffer = new MemoryStream();
                        var scratch = new byte[8192];
                        int read;
                        while ((read = await imageStream.ReadAsync(scratch).ConfigureAwait(false)) > 0)
                        {
                            if (buffer.Length + read > MaxImageBytes) throw new InvalidDataException("Oversized thumbnail");
                            buffer.Write(scratch, 0, read);
                        }
                        if (buffer.Length == 0) continue;
                        var path = CachePath(id);
                        var temp = path + ".tmp";
                        await File.WriteAllBytesAsync(temp, buffer.ToArray()).ConfigureAwait(false);
                        File.Move(temp, path, true);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
                    {
                        // Keep the existing image in cache, or show the fallback icon.
                    }
                }
            }
        }
        catch (Exception)
        {
            // Thumbnails are optional and never required for launching or session tracking.
        }
    }
}
