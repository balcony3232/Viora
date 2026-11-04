using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VioraManager;

/// <summary>
/// Persists only non-secret Roblox account identity metadata. Browser session
/// state lives in WebView2's private user-data folder and is never loaded by
/// this class or included in exported JSON.
/// </summary>
internal static class EmbeddedRobloxAccountStore
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Viora", "Manager", "accounts.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Regex RobloxUsername = new(
        @"^[A-Za-z0-9_]{3,20}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static SavedRobloxAccount SaveDetectedAccount(
        string accountId, RobloxEmbeddedLoginWindow.DetectedRobloxUser detected)
    {
        if (!Guid.TryParseExact(accountId, "N", out _) || detected.UserId <= 0 ||
            !RobloxUsername.IsMatch(detected.Username))
            throw new ArgumentException("Roblox identity or account shortcut ID is invalid.");

        var records = Load();
        var current = records.FirstOrDefault(x => x.Id == accountId);
        if (records.Any(x => x.Id != accountId &&
            (x.SessionIdentityUserId == detected.UserId ||
             x.VerifiedRobloxUserId == detected.UserId ||
             string.Equals(x.RobloxUsername, detected.Username, StringComparison.OrdinalIgnoreCase) &&
             x.PublicUserId == detected.UserId)))
            throw new InvalidOperationException("That Roblox account is already saved in Viora Manager.");

        // Preserve custom aliases, favourite/pin markers, groups and saved game
        // when updating an existing shortcut. Don't leave the old OAuth badge
        // implying a different user was linked.
        var target = current ?? new SavedRobloxAccount
        {
            Id = accountId,
            Username = detected.Username,
            Browser = "Edge",
            ProfileDirectory = "Default",
            ManagedBrowserProfile = true
        };

        string candidate = detected.Username;
        if (records.Any(a => a.Id != accountId &&
            a.Username.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            if (current is null)
                throw new InvalidOperationException(
                    "That username is already used by a saved account shortcut. Edit or remove it first.");
            candidate = target.Username;
        }
        if (current is null ||
            Regex.IsMatch(target.Username, @"^Account\d+$", RegexOptions.IgnoreCase))
            target.Username = candidate;

        target.RobloxUsername = detected.Username;
        target.PublicDisplayName = detected.DisplayName;
        target.PublicUserId = detected.UserId;
        target.SessionIdentityUserId = detected.UserId;
        target.SessionIdentityCheckedAtUtc = DateTime.UtcNow;
        target.EmbeddedBrowserProfile = true;
        target.ManagedBrowserProfile = true;

        if (target.VerifiedRobloxUserId != detected.UserId)
        {
            target.VerifiedRobloxUserId = null;
            target.VerifiedAtUtc = null;
        }

        if (current is null)
        {
            if (records.Count >= 1000)
                throw new InvalidOperationException("The limit of 1,000 saved accounts has been reached.");
            records.Add(target);
        }

        // Save atomically without replacing unreadable data. Keep one .bak of
        // the last known file for recovery, as in the existing Manager.
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(records, JsonOptions));
            if (File.Exists(FilePath))
                File.Copy(FilePath, FilePath + ".bak", overwrite: true);
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException) { }
        }

        return target;
    }

    private static List<SavedRobloxAccount> Load()
    {
        if (!File.Exists(FilePath)) return new List<SavedRobloxAccount>();
        if (new FileInfo(FilePath).Length > 1024 * 1024)
            throw new InvalidDataException("Existing accounts file exceeds 1 MB; it was preserved.");
        var records = JsonSerializer.Deserialize<List<SavedRobloxAccount>>(
            File.ReadAllText(FilePath), JsonOptions) ??
            throw new InvalidDataException("Existing account data couldn't be read; it was preserved.");
        if (records.Count > 1000 || records.Any(x => x is null ||
            !Guid.TryParseExact(x.Id, "N", out _)))
            throw new InvalidDataException("Existing account data is invalid; it was preserved.");
        return records;
    }
}
