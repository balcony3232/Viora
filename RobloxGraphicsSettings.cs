using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Viora;

/// <summary>Only edits documented user-facing values in the existing Roblox settings XML.
/// Does not inject into Roblox, alter unrelated settings or create a synthetic Roblox configuration.</summary>
public static class RobloxGraphicsSettings
{
    public sealed record Snapshot(int? Fps, int? Quality, bool HasFile);
    public sealed class Ledger
    {
        public Dictionary<string, string> OriginalValues { get; set; } = new(StringComparer.Ordinal);
    }
    public static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "GlobalBasicSettings_13.xml");
    private static string LedgerFile => Path.Combine(Storage.BackupsDir, "graphics-settings-ledger.json");
    private static string BackupFile => Path.Combine(Storage.BackupsDir, "GlobalBasicSettings_13.before-viora.xml");
    private static readonly string[] Keys = { "FramerateCap", "SavedQualityLevel" };

    private static XDocument Load(string file)
    {
        if (!File.Exists(file)) throw new FileNotFoundException(
            "Saved Roblox settings weren't found. Run Roblox once, close it, and press Reload from Roblox.", file);
        var options = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var stream = File.OpenRead(file);
        using var reader = XmlReader.Create(stream, options);
        var doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (doc.Root?.Name.LocalName != "roblox") throw new InvalidDataException("Roblox settings XML has an unexpected format. No changes were made.");
        return doc;
    }
    // Only the local user's UserGameSettings properties, never a same-named setting elsewhere.
    private static XElement? FindProperty(XDocument doc, string name)
        => doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Item" &&
               string.Equals((string?)e.Attribute("class"), "UserGameSettings", StringComparison.Ordinal))
           ?.Descendants().FirstOrDefault(e => (string?)e.Attribute("name") == name);

    private static int? ReadValue(XDocument doc, string key)
        => int.TryParse(FindProperty(doc, key)?.Value, out var result) ? result : null;

    public static Snapshot Read()
    {
        if (!File.Exists(SettingsFile)) return new(null, null, false);
        var doc = Load(SettingsFile);
        return new(ReadValue(doc, "FramerateCap"), ReadValue(doc, "SavedQualityLevel"), true);
    }

    private static void AssertRobloxClosed()
    {
        using var process = Process.GetProcessesByName("RobloxPlayerBeta").FirstOrDefault();
        if (process is not null) throw new InvalidOperationException("Close Roblox before applying or restoring graphics settings.");
    }

    private static void WriteSafely(XDocument doc)
    {
        var destination = SettingsFile;
        var temp = destination + ".viora.tmp";
        try
        {
            doc.Save(temp, SaveOptions.DisableFormatting);
            File.Move(temp, destination, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static void Apply(int fps, int quality)
    {
        if (fps is not (60 or 120 or 144 or 165 or 240)) throw new ArgumentOutOfRangeException(nameof(fps));
        if (quality is < 0 or > 10) throw new ArgumentOutOfRangeException(nameof(quality));
        AssertRobloxClosed();
        var doc = Load(SettingsFile);
        var fpsElement = FindProperty(doc, "FramerateCap");
        var qualityElement = FindProperty(doc, "SavedQualityLevel");
        if (fpsElement is null || qualityElement is null)
            throw new InvalidDataException("Roblox does not expose both FramerateCap and SavedQualityLevel in this settings file. No changes were made. Try starting and closing Roblox once, or use Roblox's own settings.");
        if (!int.TryParse(fpsElement.Value, out _) || !int.TryParse(qualityElement.Value, out _))
            throw new InvalidDataException("The saved Roblox settings use an unexpected format. No changes were made.");
        Directory.CreateDirectory(Storage.BackupsDir);
        var ledger = File.Exists(LedgerFile)
            ? JsonSerializer.Deserialize<Ledger>(File.ReadAllText(LedgerFile), Storage.JsonOptions) ?? new Ledger()
            : new Ledger();
        if (!File.Exists(BackupFile)) File.Copy(SettingsFile, BackupFile);
        foreach (var key in Keys)
        {
            var element = FindProperty(doc, key)!;
            if (!ledger.OriginalValues.ContainsKey(key)) ledger.OriginalValues[key] = element.Value;
        }
        fpsElement.Value = fps.ToString(System.Globalization.CultureInfo.InvariantCulture);
        qualityElement.Value = quality.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // Keep original values in the ledger even if user saves several different preferences.
        // Write restoration metadata first; a failed settings write must not lose the original values.
        Storage.WriteJson(LedgerFile, ledger);
        WriteSafely(doc);
    }

    public static bool HasBackup => File.Exists(LedgerFile);

    public static bool Restore()
    {
        AssertRobloxClosed();
        if (!File.Exists(LedgerFile)) return false;
        var ledger = JsonSerializer.Deserialize<Ledger>(File.ReadAllText(LedgerFile), Storage.JsonOptions)
                     ?? throw new InvalidDataException("Graphics backup could not be read.");
        var doc = Load(SettingsFile);
        foreach (var entry in ledger.OriginalValues)
        {
            if (!Keys.Contains(entry.Key, StringComparer.Ordinal)) continue;
            var element = FindProperty(doc, entry.Key)
                ?? throw new InvalidDataException("Roblox changed its settings format; the backup was retained. No changes were made.");
            element.Value = entry.Value;
        }
        WriteSafely(doc);
        File.Delete(LedgerFile);
        return true;
    }
}
