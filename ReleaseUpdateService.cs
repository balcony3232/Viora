using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Viora;

public sealed record UpdateRelease(string Version, string DetailsUrl, string ExecutableUrl, string ChecksumUrl, string FileName);
public sealed record VerifiedUpdate(string FilePath, string Sha256);

/// <summary>Checks a user-configured public GitHub repository. Downloads are verified before a separate user-approved installer is launched.</summary>
public static class ReleaseUpdateService
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = true })
    { Timeout = TimeSpan.FromSeconds(55) };
    static ReleaseUpdateService() => Client.DefaultRequestHeaders.UserAgent.ParseAdd("Viora/1.0.0 (+desktop updater)");
    public static bool TryNormalizeRepo(string input, out string repo)
    {
        repo = input.Trim().Trim('/');
        if (repo.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            repo = repo["https://github.com/".Length..].Trim('/');
        return Regex.IsMatch(repo, @"^[A-Za-z0-9_.-]{1,39}/[A-Za-z0-9_.-]{1,100}$") &&
               !repo.Contains("..", StringComparison.Ordinal);
    }

    public static async Task<UpdateRelease> GetLatestAsync(string repo)
    {
        if (!TryNormalizeRepo(repo, out var normalized))
            throw new ArgumentException("Enter a GitHub repository as owner/repo, or its github.com URL.");
        var uri = new Uri($"https://api.github.com/repos/{normalized}/releases/latest");
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("No published GitHub Release was found. Publish a tagged release first.");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;
        var version = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
        if (!Version.TryParse(version, out _)) throw new InvalidDataException("Release tag is not a version number.");
        string details = root.GetProperty("html_url").GetString() ?? "";
        if (!IsGithubHttps(details)) throw new InvalidDataException("Invalid release page URL.");
        var assets = root.GetProperty("assets").EnumerateArray().Select(a => (
            Name: a.GetProperty("name").GetString() ?? "",
            Url: a.GetProperty("browser_download_url").GetString() ?? "")).ToList();
        var expected = $"Viora-v{version}.exe";
        var exe = assets.FirstOrDefault(a => a.Name.Equals(expected, StringComparison.OrdinalIgnoreCase));
        var sha = assets.FirstOrDefault(a => a.Name.Equals(expected + ".sha256", StringComparison.OrdinalIgnoreCase));
        if (exe.Name is null || sha.Name is null || !IsGithubHttps(exe.Url) || !IsGithubHttps(sha.Url))
            throw new InvalidDataException("The release must include Viora-vVERSION.exe and its .sha256 asset.");
        return new UpdateRelease(version, details, exe.Url, sha.Url, expected);
    }
    private static bool IsGithubHttps(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
           uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    public static bool IsNewer(string version)
        => Version.TryParse(version, out var latest) &&
           latest > (typeof(ReleaseUpdateService).Assembly.GetName().Version ?? new Version(0, 0));

    public static async Task<VerifiedUpdate> DownloadVerifiedAsync(UpdateRelease release, IProgress<int>? progress = null)
    {
        // The checksum is read from the same release, never guessed or skipped.
        var checksumBody = await Client.GetStringAsync(release.ChecksumUrl);
        var match = Regex.Match(checksumBody, @"(?im)^([a-f0-9]{64})\s+\*?" + Regex.Escape(release.FileName) + @"\s*$");
        if (!match.Success) throw new InvalidDataException("Release SHA-256 checksum file is invalid.");
        var expectedSha = match.Groups[1].Value;
        var stage = Path.Combine(Storage.Root, "updates", "staged");
        Directory.CreateDirectory(stage);
        var destination = Path.Combine(stage, release.FileName);
        var temp = destination + ".part";
        try
        {
            using var response = await Client.GetAsync(release.ExecutableUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 350_000_000)
                throw new InvalidDataException("Release executable exceeds the 350 MB download limit.");
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[64 * 1024];
                long total = 0;
                int n;
                while ((n = await source.ReadAsync(buffer)) > 0)
                {
                    total += n;
                    if (total > 350_000_000) throw new InvalidDataException("Release exceeds download limit.");
                    await target.WriteAsync(buffer.AsMemory(0, n));
                    if (response.Content.Headers.ContentLength is long length && length > 0)
                        progress?.Report((int)Math.Clamp(total * 100 / length, 0, 100));
                }
            }
            string actual;
            using (var input = File.OpenRead(temp))
                actual = Convert.ToHexString(SHA256.HashData(input));
            if (!actual.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 verification failed. Download was discarded.");
            File.Move(temp, destination, true);
            progress?.Report(100);
            return new VerifiedUpdate(destination, actual.ToLowerInvariant());
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
