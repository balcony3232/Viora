using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace Viora;

/// <summary>
/// A short-lived PowerShell helper performs file replacement only after Viora exits.
/// No elevation, scheduled task, browser credentials or Roblox modification is involved.
/// The user's explicit confirmation is required before this helper is started.
/// </summary>
internal static class UpdateInstallerService
{
    private const string Script = """
param(
    [Parameter(Mandatory=$true)][string]$InstalledExe,
    [Parameter(Mandatory=$true)][string]$StagedExe,
    [Parameter(Mandatory=$true)][string]$ExpectedSha,
    [Parameter(Mandatory=$true)][int]$ParentProcess,
    [Parameter(Mandatory=$true)][string]$StatusLog
)
$ErrorActionPreference = 'Stop'
function Write-UpdateLog([string]$Message) {
    Add-Content -LiteralPath $StatusLog -Value ("{0:u}  {1}" -f (Get-Date), $Message)
}
try {
    Write-UpdateLog 'Waiting for Viora to close.'
    for ($i=0; $i -lt 150; $i++) {
        if (-not (Get-Process -Id $ParentProcess -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 400
    }
    if (Get-Process -Id $ParentProcess -ErrorAction SilentlyContinue) {
        throw 'Viora did not close within 60 seconds; existing installation was not changed.'
    }
    if (-not (Test-Path -LiteralPath $InstalledExe -PathType Leaf)) { throw 'Installed executable no longer exists.' }
    if (-not (Test-Path -LiteralPath $StagedExe -PathType Leaf)) { throw 'Staged executable no longer exists.' }
    $actual = (Get-FileHash -LiteralPath $StagedExe -Algorithm SHA256).Hash
    if ($actual -ne $ExpectedSha) { throw 'Staged executable failed its SHA-256 verification.' }
    $backup = "$InstalledExe.previous"
    Copy-Item -LiteralPath $InstalledExe -Destination $backup -Force -ErrorAction Stop
    Write-UpdateLog ('Backup created at ' + $backup)
    try {
        Copy-Item -LiteralPath $StagedExe -Destination $InstalledExe -Force -ErrorAction Stop
        $installedHash = (Get-FileHash -LiteralPath $InstalledExe -Algorithm SHA256).Hash
        if ($installedHash -ne $ExpectedSha) { throw 'Installed executable failed its SHA-256 verification.' }
        Write-UpdateLog 'Replacement verified, launching Viora.'
        Start-Process -FilePath $InstalledExe -ErrorAction Stop
        Write-UpdateLog 'Update completed successfully.'
    }
    catch {
        Write-UpdateLog ('Installation failed, restoring previous executable: ' + $_)
        Copy-Item -LiteralPath $backup -Destination $InstalledExe -Force -ErrorAction Stop
        throw
    }
}
catch {
    Write-UpdateLog ('Update failed: ' + $_)
    exit 1
}
""";

    public static void Start(VerifiedUpdate update)
    {
        string currentExe = Environment.ProcessPath ?? throw new InvalidOperationException("Executable location is unavailable.");
        currentExe = Path.GetFullPath(currentExe);
        if (!File.Exists(currentExe) || !currentExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Viora must be running as an installed Windows EXE to update itself.");

        string staged = Path.GetFullPath(update.FilePath);
        string stagingRoot = Path.GetFullPath(Path.Combine(Storage.Root, "updates", "staged"))
            + Path.DirectorySeparatorChar;
        if (!staged.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(staged))
            throw new InvalidOperationException("The update must be staged in Viora's local update folder.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(update.Sha256, "^[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("Update checksum is invalid.");

        using (var file = File.OpenRead(staged))
        {
            var hash = Convert.ToHexString(SHA256.HashData(file));
            if (!hash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update checksum changed after download. Installation was cancelled.");
        }
        string helperDir = Path.Combine(Storage.Root, "updates", "installer");
        Directory.CreateDirectory(helperDir);
        string scriptFile = Path.Combine(helperDir, "apply-update.ps1");
        string logFile = Path.Combine(helperDir, "update.log");
        File.WriteAllText(scriptFile, Script);

        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) throw new FileNotFoundException("Windows PowerShell is unavailable.", powershell);
        var info = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = helperDir
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(scriptFile);
        info.ArgumentList.Add("-InstalledExe");
        info.ArgumentList.Add(currentExe);
        info.ArgumentList.Add("-StagedExe");
        info.ArgumentList.Add(staged);
        info.ArgumentList.Add("-ExpectedSha");
        info.ArgumentList.Add(update.Sha256);
        info.ArgumentList.Add("-ParentProcess");
        info.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        info.ArgumentList.Add("-StatusLog");
        info.ArgumentList.Add(logFile);
        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("Could not start the update installer.");
        // The caller must close the WPF main window immediately after this method returns.
    }
}
