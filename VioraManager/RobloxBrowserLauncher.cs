using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace VioraManager;

/// <summary>
/// Launches only official Roblox HTTPS links in a chosen installed browser
/// profile. Never reads passwords/cookies, obtains authentication tickets,
/// starts a RobloxPlayerBeta.exe with session arguments, or alters Roblox.
/// The running Roblox Player may use an account OTHER than the browser account.
/// </summary>
internal static class RobloxBrowserLauncher
{
    private static readonly Regex ProfileFolder = new(
        @"^(Default|Profile [1-9][0-9]{0,3})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static long? ParsePlaceId(string? raw)
    {
        string input = raw?.Trim() ?? "";
        if (input.Length == 0) return null;
        if (long.TryParse(input, out long number) && number > 0) return number;

        if (!Uri.TryCreate(input, UriKind.Absolute, out Uri? url) ||
            url.Scheme != Uri.UriSchemeHttps ||
            !(url.Host.Equals("roblox.com", StringComparison.OrdinalIgnoreCase) ||
              url.Host.Equals("www.roblox.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Enter a numeric Place ID or an official https://www.roblox.com/games/... URL.");
        }

        string[] parts = url.AbsolutePath.Trim('/').Split('/');
        if (parts.Length >= 2 && parts[0].Equals("games", StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(parts[1], out number) && number > 0) return number;

            // Official web-to-player deep-link URL, for copying an existing join link.
            if (parts[1].Equals("start", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string item in url.Query.TrimStart('?').Split('&'))
                {
                    var pair = item.Split('=', 2);
                    if (pair.Length == 2 && pair[0].Equals("placeId", StringComparison.OrdinalIgnoreCase) &&
                        long.TryParse(Uri.UnescapeDataString(pair[1]), out number) && number > 0)
                        return number;
                }
            }
        }
        throw new ArgumentException(
            "This link does not contain a valid Roblox Place ID. A game or subplace ID is required.");
    }

    public static void Open(SavedRobloxAccount account, long? placeId, bool tryJoinDirectly)
    {
        if (account.Browser is not ("Edge" or "Chrome" or "Brave") ||
            account.ProfileDirectory is null || !ProfileFolder.IsMatch(account.ProfileDirectory))
            throw new ArgumentException("A supported browser and profile folder are required.");

        string executable = FindInstalledBrowser(account.Browser)
            ?? throw new FileNotFoundException(account.Browser + " was not found in the standard install locations.");

        string url = placeId is long id && id > 0
            ? tryJoinDirectly
                // Documented by Roblox Creator Hub, currently marked deprecated.
                // The Roblox Player may still use an independent account session.
                ? "https://www.roblox.com/games/start?placeId=" + id
                : "https://www.roblox.com/games/" + id
            : "https://www.roblox.com/home";

        var info = new ProcessStartInfo(executable) { UseShellExecute = false };
        info.ArgumentList.Add("--profile-directory=" + account.ProfileDirectory);
        info.ArgumentList.Add("--new-window");
        info.ArgumentList.Add(url);

        using var process = Process.Start(info);
        if (process is null)
            throw new InvalidOperationException("Browser launch did not start.");
    }

    private static string? FindInstalledBrowser(string browser)
    {
        string file = browser switch
        {
            "Edge" => "msedge.exe",
            "Chrome" => "chrome.exe",
            "Brave" => "brave.exe",
            _ => throw new ArgumentException("Unsupported browser.")
        };
        string vendor = browser switch
        {
            "Edge" => @"Microsoft\Edge\Application",
            "Chrome" => @"Google\Chrome\Application",
            "Brave" => @"BraveSoftware\Brave-Browser\Application",
            _ => throw new ArgumentException("Unsupported browser.")
        };
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        foreach (string root in roots.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            string path = Path.Combine(root, vendor, file);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
