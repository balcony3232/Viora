using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Viora;

public sealed record UpdateAsset(string FileName, string ExecutableUrl, string ChecksumUrl);
public sealed record UpdateRelease(
    string Version, string DetailsUrl, string ExecutableUrl, string ChecksumUrl, string FileName,
    UpdateAsset? LightweightLauncher, UpdateAsset? Manager, UpdateAsset? LightweightManager)
{
    public UpdateAsset FullLauncher => new(FileName, ExecutableUrl, ChecksumUrl);
    public UpdateAsset SelectLauncher() => ReleaseUpdateService.HasDesktopRuntime8 && LightweightLauncher is not null
        ? LightweightLauncher : FullLauncher;
    public UpdateAsset? SelectManager() => ReleaseUpdateService.HasDesktopRuntime8 && LightweightManager is not null
        ? LightweightManager : Manager;
}
public sealed record VerifiedUpdate(string FilePath, string Sha256);

/// <summary>Checks a user-configured public GitHub repository. Downloads are verified before a separate user-approved installer is launched.</summary>
public static class ReleaseUpdateService
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = true })
    { Timeout = TimeSpan.FromSeconds(55) };
    static ReleaseUpdateService() => Client.DefaultRequestHeaders.UserAgent.ParseAdd("Viora/1.3.0 (+desktop updater)");

    // Framework-dependent EXEs must never be offered without this prerequisite.
    // Fall back to the original self-contained application on other machines.
    public static bool HasDesktopRuntime8
    {
        get
        {
            try
            {
                string dotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "dotnet", "shared", "Microsoft.WindowsDesktop.App");
                return Directory.Exists(dotnet) && Directory.EnumerateDirectories(dotnet)
                    .Any(path => Version.TryParse(Path.GetFileName(path), out var version) &&
                                 version.Major == 8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return false;
            }
        }
    }
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

        // The Releases list includes prereleases, unlike /releases/latest.
        // This allows beta testers to receive versioned Viora updates.
        var uri = new Uri($"https://api.github.com/repos/{normalized}/releases?per_page=30");
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("GitHub repository or releases could not be found.");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Unexpected GitHub Releases response.");

        var releases = new List<(Version ParsedVersion, JsonElement Data)>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            var rawTag = item.GetProperty("tag_name").GetString() ?? "";
            var versionText = rawTag.TrimStart('v', 'V');
            if (Version.TryParse(versionText, out var parsedVersion))
                releases.Add((parsedVersion, item));
        }
        if (releases.Count == 0)
            throw new InvalidOperationException("No published Viora releases found. Publish a versioned GitHub Release first.");

        var newest = releases.OrderByDescending(x => x.ParsedVersion).First();
        var root = newest.Data;
        string version = newest.ParsedVersion.ToString();
        string details = root.GetProperty("html_url").GetString() ?? "";
        if (!IsGithubHttps(details)) throw new InvalidDataException("Invalid release page URL.");
        var assets = root.GetProperty("assets").EnumerateArray().Select(a => (
            Name: a.GetProperty("name").GetString() ?? "",
            Url: a.GetProperty("browser_download_url").GetString() ?? "")).ToList();
        UpdateAsset? Find(string filename)
        {
            var binary = assets.FirstOrDefault(a => a.Name.Equals(filename, StringComparison.OrdinalIgnoreCase));
            var checksum = assets.FirstOrDefault(a => a.Name.Equals(filename + ".sha256", StringComparison.OrdinalIgnoreCase));
            return binary.Name is not null && checksum.Name is not null &&
                IsGithubHttps(binary.Url) && IsGithubHttps(checksum.Url)
                ? new UpdateAsset(filename, binary.Url, checksum.Url) : null;
        }

        string mainName = $"Viora-v{version}.exe";
        UpdateAsset main = Find(mainName)
            ?? throw new InvalidDataException("The newest release must contain the main Viora executable and its .sha256 asset.");
        return new UpdateRelease(version, details, main.ExecutableUrl, main.ChecksumUrl, main.FileName,
            Find($"Viora-v{version}-Lite.exe"),
            Find($"VioraManager-v{version}.exe"),
            Find($"VioraManager-v{version}-Lite.exe"));
    }
    private static bool IsGithubHttps(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
           uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    public static bool IsNewer(string version)
        => Version.TryParse(version, out var latest) &&
           latest > (typeof(ReleaseUpdateService).Assembly.GetName().Version ?? new Version(0, 0));

    public static async Task<VerifiedUpdate> DownloadVerifiedAsync(UpdateAsset asset, IProgress<int>? progress = null)
    {
        // Each component is verified against its own SHA-256 file.
        if (!Regex.IsMatch(asset.FileName, @"^Viora(?:Manager)?-v\d+\.\d+\.\d+(?:-Lite)?\.exe$", RegexOptions.IgnoreCase))
            throw new InvalidDataException("Unexpected update asset filename.");
        var checksumBody = await Client.GetStringAsync(asset.ChecksumUrl);
        var match = Regex.Match(checksumBody, @"(?im)^([a-f0-9]{64})\s+\*?" + Regex.Escape(asset.FileName) + @"\s*$");
        if (!match.Success) throw new InvalidDataException("Release SHA-256 checksum file is invalid.");
        var expectedSha = match.Groups[1].Value;
        var stage = Path.Combine(Storage.Root, "updates", "staged");
        Directory.CreateDirectory(stage);
        var destination = Path.Combine(stage, asset.FileName);
        var temp = destination + ".part";
        if (File.Exists(destination))
        {
            string existing = await Task.Run(() =>
            {
                using var stream = File.OpenRead(destination);
                return Convert.ToHexString(SHA256.HashData(stream));
            });
            if (existing.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(100);
                return new VerifiedUpdate(destination, existing.ToLowerInvariant());
            }
        }
        try
        {
            using var response = await Client.GetAsync(asset.ExecutableUrl, HttpCompletionOption.ResponseHeadersRead);
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
