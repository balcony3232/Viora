using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace Viora;

public sealed class RobloxInstallation
{
    public string DirectoryPath { get; init; } = "";
    public string Version => Path.GetFileName(DirectoryPath);
    public string Executable => Path.Combine(DirectoryPath, "RobloxPlayerBeta.exe");
    public override string ToString() => Version;
}

public static class RobloxService
{
    public static List<RobloxInstallation> FindInstallations()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");
        if (!Directory.Exists(root)) return new();
        return Directory.EnumerateDirectories(root, "version-*")
            .Where(p => File.Exists(Path.Combine(p, "RobloxPlayerBeta.exe")))
            .OrderByDescending(p => File.GetLastWriteTimeUtc(Path.Combine(p, "RobloxPlayerBeta.exe")))
            .Select(p => new RobloxInstallation { DirectoryPath = p }).ToList();
    }

    public static string FlagsPath(RobloxInstallation install) => Path.Combine(install.DirectoryPath, "ClientSettings", "ClientAppSettings.json");
    private static string LedgerPath(RobloxInstallation install) => Path.Combine(Storage.AppliedDir, install.Version + ".json");

    private static Dictionary<string, string> ReadFlags(string path)
    {
        if (!File.Exists(path)) return new(StringComparer.Ordinal);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("ClientAppSettings.json must contain a JSON object.");
        return json.RootElement.EnumerateObject().ToDictionary(
            kv => kv.Name, kv => kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() ?? "" : kv.Value.ToString(),
            StringComparer.Ordinal);
    }

    public static void ApplyFlags(RobloxInstallation install, UserProfile profile, IReadOnlyCollection<string> allowedNames)
    {
        var path = FlagsPath(install);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Leave a pristine copy before making our first edit of a client version.
        var originalSnapshot = Path.Combine(Storage.BackupsDir, install.Version, "ClientAppSettings.before-viora.json");
        if (File.Exists(path) && !File.Exists(originalSnapshot))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(originalSnapshot)!);
            File.Copy(path, originalSnapshot);
        }
        var flags = ReadFlags(path);
        var ledgerPath = LedgerPath(install);
        var oldLedger = File.Exists(ledgerPath)
            ? JsonSerializer.Deserialize<AppliedFlagLedger>(File.ReadAllText(ledgerPath), Storage.JsonOptions) ?? new AppliedFlagLedger()
            : new AppliedFlagLedger();
        foreach (var previous in oldLedger.OriginalValues)
        {
            if (previous.Value is null) flags.Remove(previous.Key);
            else flags[previous.Key] = previous.Value;
        }
        var nextLedger = new AppliedFlagLedger();
        var allowed = new HashSet<string>(allowedNames, StringComparer.Ordinal);
        foreach (var (name, value) in profile.Flags)
        {
            if (!allowed.Contains(name)) continue;
            nextLedger.OriginalValues[name] = flags.TryGetValue(name, out var existing) ? existing : null;
            flags[name] = value;
        }
        Storage.WriteJson(path, flags);
        Storage.WriteJson(ledgerPath, nextLedger);
    }

    public static void UndoFlagChanges(RobloxInstallation install)
    {
        var path = FlagsPath(install);
        var ledgerPath = LedgerPath(install);
        if (!File.Exists(ledgerPath)) return;
        var flags = ReadFlags(path);
        var ledger = JsonSerializer.Deserialize<AppliedFlagLedger>(File.ReadAllText(ledgerPath), Storage.JsonOptions) ?? new();
        foreach (var pair in ledger.OriginalValues)
        {
            if (pair.Value is null) flags.Remove(pair.Key);
            else flags[pair.Key] = pair.Value;
        }
        Storage.WriteJson(path, flags);
        File.Delete(ledgerPath);
    }

    public static void OpenRoblox()
    {
        // Delegate updating, authentication and game launching to the installed official Roblox client.
        try { Process.Start(new ProcessStartInfo("roblox://") { UseShellExecute = true }); }
        catch { OpenUrl("https://www.roblox.com/home"); }
    }
    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });


    // Public tracker lists externally visible client settings, not the private/internal complete universe.
    public static async Task<List<FlagDefinition>> DownloadPublicFlagNamesAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25), MaxResponseContentBufferSize = 12_000_000 };
        using var stream = await client.GetStreamAsync("https://raw.githubusercontent.com/MaximumADHD/Roblox-FFlag-Tracker/main/PCDesktopClient.json");
        using var json = await JsonDocument.ParseAsync(stream);
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The public flag tracker changed its format.");
        var results = new List<FlagDefinition>();
        foreach (var property in json.RootElement.EnumerateObject())
        {
            var name = property.Name;
            if (name.Length > 160 || !(name.StartsWith("FFlag", StringComparison.Ordinal) ||
                name.StartsWith("DFFlag", StringComparison.Ordinal) || name.StartsWith("SFFlag", StringComparison.Ordinal) ||
                name.StartsWith("FInt", StringComparison.Ordinal) || name.StartsWith("DFInt", StringComparison.Ordinal) ||
                name.StartsWith("SFInt", StringComparison.Ordinal) || name.StartsWith("FString", StringComparison.Ordinal) ||
                name.StartsWith("DFString", StringComparison.Ordinal) || name.StartsWith("SFString", StringComparison.Ordinal) ||
                name.StartsWith("FLog", StringComparison.Ordinal))) continue;
            var type = name.Contains("Flag", StringComparison.Ordinal) ? "Boolean" :
                name.Contains("Int", StringComparison.Ordinal) ? "Integer" :
                name.Contains("String", StringComparison.Ordinal) ? "String" : "Unknown";
            results.Add(new FlagDefinition
            {
                Name = name, Category = "Public reference", Type = type,
                Description = "This name appears in a public tracker of Roblox client settings. Its exact behaviour is not verified or explained in public documentation. It may no longer exist in your current client version.",
                Warning = "Reference only. Roblox will ignore local overrides not on its current allowlist.",
                Confidence = "Third-party public settings tracker; function undocumented",
                IsAllowlisted = false
            });
        }
        return results;
    }

    public static async Task<string> CheckLatestVersionAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        var raw = await client.GetStringAsync("https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer");
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.GetProperty("clientVersionUpload").GetString() ?? "Unknown";
    }
}

public static class ModManager
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".png", ".jpg", ".jpeg", ".dds", ".webp", ".ogg", ".wav", ".mp3", ".ttf", ".otf" };

    public static string ValidateTarget(string target)
    {
        var normal = target.Replace('\\', '/').Trim().TrimStart('/');
        if (!normal.StartsWith("content/", StringComparison.OrdinalIgnoreCase) || normal.Contains("..", StringComparison.Ordinal) || normal.EndsWith('/'))
            throw new ArgumentException("Target must start with content/ and must not contain '..'. Example: content/sounds/ouch.ogg");
        if (!AllowedExtensions.Contains(Path.GetExtension(normal))) throw new ArgumentException("Only supported image, sound and font asset extensions are allowed.");
        return normal;
    }

    private static string TargetPath(RobloxInstallation install, string relative)
    {
        var safe = ValidateTarget(relative);
        var full = Path.GetFullPath(Path.Combine(install.DirectoryPath, safe.Replace('/', Path.DirectorySeparatorChar)));
        var content = Path.GetFullPath(Path.Combine(install.DirectoryPath, "content")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(content, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The asset target is outside the Roblox content directory.");
        return full;
    }

    private static string BackupPath(RobloxInstallation install, string relative)
       => Path.Combine(Storage.BackupsDir, install.Version, "assets", ValidateTarget(relative).Replace('/', Path.DirectorySeparatorChar));

    public static ModEntry StageAsset(string sourceFile, string relativeTarget)
    {
        if (!File.Exists(sourceFile)) throw new FileNotFoundException("Choose an existing asset file.", sourceFile);
        var normal = ValidateTarget(relativeTarget);
        var sourceExtension = Path.GetExtension(sourceFile);
        if (!AllowedExtensions.Contains(sourceExtension) || !sourceExtension.Equals(Path.GetExtension(normal), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The source and destination must have the same supported file extension.");
        Directory.CreateDirectory(Storage.ModsDir);
        var id = Guid.NewGuid().ToString("N");
        var saved = Path.Combine(Storage.ModsDir, id + sourceExtension.ToLowerInvariant());
        File.Copy(sourceFile, saved);
        return new ModEntry { Id = id, Label = Path.GetFileName(sourceFile), SavedFile = saved, RelativeTarget = normal };
    }

    public static int ApplyAssets(RobloxInstallation install, IEnumerable<ModEntry> mods)
    {
        var count = 0;
        foreach (var mod in mods.Where(x => x.Enabled))
        {
            var target = TargetPath(install, mod.RelativeTarget);
            if (!File.Exists(mod.SavedFile)) throw new FileNotFoundException("A staged asset is missing: " + mod.Label);
            // Only replace resources that the installed client already contains.
            if (!File.Exists(target)) throw new FileNotFoundException("The Roblox version does not contain this target asset: " + mod.RelativeTarget);
            var backup = BackupPath(install, mod.RelativeTarget);
            if (!File.Exists(backup))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(target, backup);
            }
            File.Copy(mod.SavedFile, target, true);
            count++;
        }
        return count;
    }

    public static int RestoreAssets(RobloxInstallation install, IEnumerable<ModEntry> mods)
    {
        var count = 0;
        foreach (var mod in mods)
        {
            var backup = BackupPath(install, mod.RelativeTarget);
            if (!File.Exists(backup)) continue;
            var target = TargetPath(install, mod.RelativeTarget);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(backup, target, true);
            File.Delete(backup);
            count++;
        }
        return count;
    }

    public static void ExportConfiguration(AppState state, string destination)
    {
        using var zip = ZipFile.Open(destination, ZipArchiveMode.Create);
        var raw = JsonSerializer.Serialize(state, Storage.JsonOptions);
        var entry = zip.CreateEntry("settings.json");
        using (var writer = new StreamWriter(entry.Open())) writer.Write(raw);
        if (File.Exists(Storage.ExtraCatalogPath)) zip.CreateEntryFromFile(Storage.ExtraCatalogPath, "catalog-extra.json");
        foreach (var mod in state.Mods.Where(x => File.Exists(x.SavedFile)))
            zip.CreateEntryFromFile(mod.SavedFile, "mods/" + Path.GetFileName(mod.SavedFile));
    }
}
