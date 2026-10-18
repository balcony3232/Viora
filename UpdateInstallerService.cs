using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace Viora;

/// <summary>
/// User-approved, short-lived PowerShell updater. It waits for Viora to exit,
/// verifies all staged payloads, keeps one rollback copy per application and
/// restarts Viora. Manager updates require Manager to be completely closed.
/// Neither executable modifies Roblox files or Windows system locations.
/// </summary>
internal static class UpdateInstallerService
{
    private const string Script = """
param(
    [Parameter(Mandatory=$true)][string]$InstalledExe,
    [string]$StagedExe = '',
    [string]$ExpectedSha = '',
    [string]$InstalledManagerExe = '',
    [string]$StagedManagerExe = '',
    [string]$ManagerSha = '',
    [Parameter(Mandatory=$true)][int]$ParentProcess,
    [Parameter(Mandatory=$true)][string]$StatusLog
)
$ErrorActionPreference = 'Stop'
function Write-UpdateLog([string]$Message) {
    Add-Content -LiteralPath $StatusLog -Value ("{0:u}  {1}" -f (Get-Date), $Message)
}
function Test-Checksum([string]$File, [string]$Checksum) {
    if (-not (Test-Path -LiteralPath $File -PathType Leaf)) { throw ('File missing: ' + $File) }
    if ((Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash -ne $Checksum) {
        throw ('SHA-256 verification failed: ' + $File)
    }
}
$mainChanged = $false
$managerChanged = $false
$mainBackup = "$InstalledExe.previous"
$managerBackup = "$InstalledManagerExe.previous"
try {
    Write-UpdateLog 'Waiting for Viora to exit.'
    for ($i=0; $i -lt 150; $i++) {
        if (-not (Get-Process -Id $ParentProcess -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 400
    }
    if (Get-Process -Id $ParentProcess -ErrorAction SilentlyContinue) {
        throw 'Viora did not exit within 60 seconds.'
    }
    if (-not (Test-Path -LiteralPath $InstalledExe -PathType Leaf)) {
        throw 'Existing Viora executable was not found.'
    }
    if ($StagedManagerExe -ne '') {
        if (Get-Process -Name 'VioraManager' -ErrorAction SilentlyContinue) {
            throw 'Close Viora Manager before installing its update.'
        }
        if (-not (Test-Path -LiteralPath $InstalledManagerExe -PathType Leaf)) {
            throw 'Existing Viora Manager executable was not found.'
        }
        Test-Checksum $StagedManagerExe $ManagerSha
    }
    if ($StagedExe -ne '') { Test-Checksum $StagedExe $ExpectedSha }

    # Make backups before touching either executable.
    if ($StagedExe -ne '') {
        Copy-Item -LiteralPath $InstalledExe -Destination $mainBackup -Force
    }
    if ($StagedManagerExe -ne '') {
        Copy-Item -LiteralPath $InstalledManagerExe -Destination $managerBackup -Force
    }

    try {
        if ($StagedExe -ne '') {
            $mainChanged = $true
            Copy-Item -LiteralPath $StagedExe -Destination $InstalledExe -Force
            Test-Checksum $InstalledExe $ExpectedSha
        }
        if ($StagedManagerExe -ne '') {
            $managerChanged = $true
            Copy-Item -LiteralPath $StagedManagerExe -Destination $InstalledManagerExe -Force
            Test-Checksum $InstalledManagerExe $ManagerSha
        }
        Write-UpdateLog 'Installed files verified; restarting Viora.'
        Start-Process -FilePath $InstalledExe -ErrorAction Stop
    }
    catch {
        Write-UpdateLog ('Installation failed; restoring old versions: ' + $_)
        if ($managerChanged -and (Test-Path -LiteralPath $managerBackup)) {
            Copy-Item -LiteralPath $managerBackup -Destination $InstalledManagerExe -Force
        }
        if ($mainChanged -and (Test-Path -LiteralPath $mainBackup)) {
            Copy-Item -LiteralPath $mainBackup -Destination $InstalledExe -Force
        }
        throw
    }

    # Do not leave full-size staged copies after a successful update.
    if ($StagedExe -ne '') { Remove-Item -LiteralPath $StagedExe -Force -ErrorAction SilentlyContinue }
    if ($StagedManagerExe -ne '') { Remove-Item -LiteralPath $StagedManagerExe -Force -ErrorAction SilentlyContinue }
    Write-UpdateLog 'Update completed. Previous EXEs are retained as .previous rollback backups.'
}
catch {
    Write-UpdateLog ('Update failed: ' + $_)
    exit 1
}
""";

    private static string ValidateStaged(VerifiedUpdate update)
    {
        string staged = Path.GetFullPath(update.FilePath);
        string root = Path.GetFullPath(Path.Combine(Storage.Root, "updates", "staged"))
            + Path.DirectorySeparatorChar;
        if (!staged.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(staged))
            throw new InvalidOperationException("The update was not staged in Viora's private updates folder.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(update.Sha256, "^[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("Invalid update checksum.");

        using var file = File.OpenRead(staged);
        string actual = Convert.ToHexString(SHA256.HashData(file));
        if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The staged update checksum changed. Installation cancelled.");
        return staged;
    }

    public static void Start(VerifiedUpdate? launcher, VerifiedUpdate? manager)
    {
        if (launcher is null && manager is null)
            throw new ArgumentException("Select at least one update component.");

        string currentExe = Path.GetFullPath(Environment.ProcessPath ??
            throw new InvalidOperationException("The running Viora executable path is unavailable."));
        if (!File.Exists(currentExe) || !currentExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Viora must be launched from a Windows EXE to update.");

        string managerExe = Path.Combine(Path.GetDirectoryName(currentExe)!, "VioraManager.exe");
        if (manager is not null)
        {
            if (!File.Exists(managerExe))
                throw new FileNotFoundException("An installed VioraManager.exe was not found beside Viora.", managerExe);
            var processes = Process.GetProcessesByName("VioraManager");
            try
            {
                if (processes.Any(p => { try { return !p.HasExited; } catch { return false; } }))
                    throw new InvalidOperationException("Close Viora Manager before updating it. The active multi-instance session may be interrupted.");
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }

        string? mainStage = launcher is null ? null : ValidateStaged(launcher);
        string? managerStage = manager is null ? null : ValidateStaged(manager);
        string helperDir = Path.Combine(Storage.Root, "updates", "installer");
        Directory.CreateDirectory(helperDir);
        string script = Path.Combine(helperDir, "apply-update.ps1");
        string log = Path.Combine(helperDir, "update.log");
        File.WriteAllText(script, Script);

        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) throw new FileNotFoundException("Windows PowerShell is unavailable.", powershell);
        var info = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = helperDir
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(script);
        info.ArgumentList.Add("-InstalledExe");
        info.ArgumentList.Add(currentExe);
        if (mainStage is not null)
        {
            info.ArgumentList.Add("-StagedExe"); info.ArgumentList.Add(mainStage);
            info.ArgumentList.Add("-ExpectedSha"); info.ArgumentList.Add(launcher!.Sha256);
        }
        if (managerStage is not null)
        {
            info.ArgumentList.Add("-InstalledManagerExe"); info.ArgumentList.Add(managerExe);
            info.ArgumentList.Add("-StagedManagerExe"); info.ArgumentList.Add(managerStage);
            info.ArgumentList.Add("-ManagerSha"); info.ArgumentList.Add(manager!.Sha256);
        }
        info.ArgumentList.Add("-ParentProcess");
        info.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        info.ArgumentList.Add("-StatusLog");
        info.ArgumentList.Add(log);

        using var process = Process.Start(info) ??
            throw new InvalidOperationException("Could not start the update helper.");
        // MainWindow closes after this call returns.
    }
}
