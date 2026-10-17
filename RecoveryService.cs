using System.IO;
using System.IO.Compression;

namespace Viora;

/// <summary>Portable local config backups and staged recovery. Never restores executable files.</summary>
public static class RecoveryService
{
    private const long MaxBytes = 250L * 1024 * 1024;
    private static string PendingDir => Path.Combine(Storage.Root, "recovery-pending");
    private static string Ready => Path.Combine(PendingDir, "ready.marker");

    private static bool IsUserData(string relative)
    {
        var r = relative.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(r) || r.StartsWith('/') || r.Contains(':') ||
            r.Split('/').Any(x => x == ".." || x == "." || x == "")) return false;
        string first = r.Split('/')[0];
        if (first is "backups" or "recovery-pending" or "game-icons" or "downloads" or "release" or "publish" or "bin" or "obj") return false;
        if (r.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            r.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            r.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) return false;
        return first is "mods" or "applied" or "splash" || first == "settings.json" ||
            first == "local-account.json" || first == "catalog-extra.json" ||
            first.StartsWith("avatar", StringComparison.OrdinalIgnoreCase) ||
            first.StartsWith("splash", StringComparison.OrdinalIgnoreCase) ||
            first.StartsWith("custom", StringComparison.OrdinalIgnoreCase);
    }

    public static int Export(string zipPath)
    {
        Directory.CreateDirectory(Storage.Root);
        var files = Directory.EnumerateFiles(Storage.Root, "*", new EnumerationOptions {
            RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Where(p => IsUserData(Path.GetRelativePath(Storage.Root, p)))
            .Where(p => !File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint))
            .ToArray();
        if (files.Sum(p => new FileInfo(p).Length) > MaxBytes)
            throw new InvalidOperationException("Local configuration exceeds the 250 MB backup limit.");
        if (files.Length > 1000) throw new InvalidOperationException("Too many files to back up.");
        if (File.Exists(zipPath)) throw new IOException("Choose a new backup filename.");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var file in files)
            archive.CreateEntryFromFile(file, Path.GetRelativePath(Storage.Root, file).Replace('\\', '/'), CompressionLevel.Optimal);
        return files.Length;
    }

    public static int StageRestore(string zipPath)
    {
        if (File.Exists(Ready)) throw new InvalidOperationException("A restore is already scheduled. Restart Viora first.");
        using var archive = ZipFile.OpenRead(zipPath);
        var entries = archive.Entries.Where(e => !string.IsNullOrWhiteSpace(e.Name)).ToList();
        if (entries.Count > 1000 || entries.Sum(x => x.Length) > MaxBytes)
            throw new InvalidDataException("Backup exceeds the recovery size/file limit.");
        if (entries.Count == 0 || !entries.Any(e => e.FullName == "settings.json"))
            throw new InvalidDataException("This is not a valid Viora configuration backup (settings.json is missing).");
        // Reject a corrupt settings file before scheduling any overwrite.
        var settingsEntry = entries.First(e => e.FullName == "settings.json");
        using (var jsonStream = settingsEntry.Open())
        using (var json = System.Text.Json.JsonDocument.Parse(jsonStream))
        {
            if (json.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                throw new InvalidDataException("Backup settings.json must contain an object.");
        }
        foreach (var entry in entries)
        {
            if (!IsUserData(entry.FullName) || entry.Length > 50_000_000)
                throw new InvalidDataException("Unsafe or unsupported backup entry: " + entry.FullName);
        }
        if (Directory.Exists(PendingDir)) Directory.Delete(PendingDir, true);
        Directory.CreateDirectory(PendingDir);
        try
        {
            foreach (var entry in entries)
            {
                var destination = Path.GetFullPath(Path.Combine(PendingDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!destination.StartsWith(PendingDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid path in backup.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination);
            }
            File.WriteAllText(Ready, "ready");
            return entries.Count;
        }
        catch { if (Directory.Exists(PendingDir)) Directory.Delete(PendingDir, true); throw; }
    }

    // Must run before Storage.Load(), on next startup.
    public static bool ApplyPendingRestore()
    {
        if (!File.Exists(Ready)) return false;
        // Preserve pre-restore settings and local identity in an emergency backup folder.
        string safety = Path.Combine(Storage.BackupsDir, "before-recovery-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(safety);
        var staged = Directory.EnumerateFiles(PendingDir, "*", SearchOption.AllDirectories)
            .Where(f => f != Ready).ToArray();
        foreach (var file in staged)
        {
            string relative = Path.GetRelativePath(PendingDir, file);
            if (!IsUserData(relative)) throw new InvalidDataException("Invalid staged recovery file.");
            var destination = Path.Combine(Storage.Root, relative);
            if (!File.Exists(destination)) continue;
            string original = Path.Combine(safety, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(original)!);
            File.Copy(destination, original);
        }
        foreach (var file in staged)
        {
            var destination = Path.Combine(Storage.Root, Path.GetRelativePath(PendingDir, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
        Directory.Delete(PendingDir, true);
        return true;
    }
}
