using System;
using System.IO;
using System.Text;

namespace Viora;

/// <summary>
/// Local, bounded diagnostic breadcrumbs. The file is never uploaded, copied
/// into reports or included in a public GitHub issue automatically.
/// Users must review/redact it if they choose to share any lines.
/// </summary>
internal static class CrashDiagnostics
{
    private static readonly object Gate = new();
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Viora", "Diagnostics");

    internal static string LogPath => Path.Combine(Folder, "crash-diagnostics.txt");

    internal static void Mark(string phase)
        => Write("Step: " + phase);

    internal static void Record(string source, Exception exception)
    {
        // Raw error messages / stack traces can include user-specific paths.
        // Keep them ONLY in the local private diagnostic file.
        Write($"Unhandled ({source})\n{exception}");
    }

    internal static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                string path = LogPath;
                if (File.Exists(path) && new FileInfo(path).Length > 128 * 1024)
                {
                    // Don't accumulate unlimited crash history across sessions.
                    File.Move(path, path + ".previous", overwrite: true);
                }
                var line = $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}";
                File.AppendAllText(path, line, new UTF8Encoding(false));
            }
        }
        catch (Exception)
        {
            // A failure to write optional diagnostics must never crash Viora.
        }
    }
}
