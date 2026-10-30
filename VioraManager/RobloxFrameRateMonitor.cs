using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace VioraManager;

/// <summary>
/// Optional non-invasive per-process render-FPS sampler. Reads Intel PresentMon
/// CSV lines from a private stdout pipe; never writes frame traces to disk.
/// Render FPS counts application Present() calls, not necessarily frames displayed.
/// </summary>
internal sealed class RobloxFrameRateMonitor : IDisposable
{
    private const string HelperName = "PresentMon-2.5.1-x64.exe";
    private readonly object _gate = new();
    private readonly Dictionary<int, List<Frame>> _frames = new();
    private Process? _capture;
    private int _pidColumn = -1;
    private int _frameMsColumn = -1;
    private int _chainColumn = -1;
    private string _status = "Not started";
    private string _lastError = "";
    private int? _exitCode;

    private bool _disposed;

    private sealed record Frame(string SwapChain, DateTime When, double IntervalMs);

    public string Status
    {
        get { lock (_gate) return _status; }
    }

    public void Start()
    {
        if (_disposed) return;
        Stop();
        var path = Path.Combine(AppContext.BaseDirectory, HelperName);
        if (!File.Exists(path))
        {
            lock (_gate) _status = "PresentMon helper missing — extract ALL files from the preview ZIP.";
            return;
        }

        var start = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        // The --process_name filter covers every Roblox Player PID. The unique
        // trace session avoids terminating someone else's PresentMon session.
        foreach (var argument in new[]
        {
            "--process_name", "RobloxPlayerBeta.exe",
            "--output_stdout", "--no_console_stats",
            "--no_track_gpu", "--no_track_input", "--no_track_display",
            "--session_name", "VioraManagerFPS_" + Environment.ProcessId
        })
            start.ArgumentList.Add(argument);

        try
        {
            var process = new Process { StartInfo = start, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) FeedCsvLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                var diagnostic = e.Data.Trim();
                // Preserve an actionable error rather than replacing it with
                // a generic message when the process exits.
                if (diagnostic.Length > 220) diagnostic = diagnostic[..220];
                lock (_gate)
                {
                    if (_disposed || !ReferenceEquals(_capture, process)) return;
                    if (diagnostic.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                        diagnostic.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
                        diagnostic.Contains("failed", StringComparison.OrdinalIgnoreCase))
                    {
                        _lastError = diagnostic;
                        _status = FormatExitStatus();
                    }
                }
            };
            process.Exited += (_, _) =>
            {
                int? code;
                try { code = process.ExitCode; }
                catch (InvalidOperationException) { code = null; }
                lock (_gate)
                {
                    if (_disposed || !ReferenceEquals(_capture, process)) return;
                    _exitCode = code;
                    _status = FormatExitStatus();
                }
            };
            lock (_gate)
            {
                _pidColumn = _frameMsColumn = _chainColumn = -1;
                _frames.Clear();
                _lastError = "";
                _exitCode = null;
                _status = "FPS sampler starting...";
                _capture = process;
            }
            if (!process.Start())
            {
                lock (_gate) _status = "PresentMon could not start.";
                return;
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            lock (_gate) _status = "FPS capture unavailable: " + ex.Message;
        }
    }

    private string FormatExitStatus()
    {
        // Caller holds _gate. Never claim that missing FPS means Roblox was
        // blocked; Windows trace access, capture options and helper startup vary.
        if (_lastError.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
            _lastError.Contains("permission", StringComparison.OrdinalIgnoreCase))
            return "FPS access denied by Windows. PresentMon requires trace permission (Performance Log Users or approved elevation). " +
                   _lastError;
        if (!string.IsNullOrWhiteSpace(_lastError))
            return "PresentMon: " + _lastError + (_exitCode is int c ? " (exit " + c + ")" : "");
        return _exitCode is int exit
            ? "PresentMon exited (code " + exit + "). Use FPS diagnostics for help."
            : "PresentMon reported an error. Use FPS diagnostics for help.";
    }

    public string DiagnosticSummary()
    {
        lock (_gate)
        {
            return "PresentMon 2.5.1 (official console helper)\\n" +
                "Status: " + _status + "\\n" +
                "Exit code: " + (_exitCode?.ToString(CultureInfo.InvariantCulture) ?? "not exited") + "\\n" +
                "Last helper error: " + (string.IsNullOrEmpty(_lastError) ? "none received" : _lastError) + "\\n" +
                "FPS data headers: " + (_pidColumn >= 0 ? "received" : "not received") + "\\n" +
                "Captured Roblox PIDs: " + _frames.Count + "\\n\\n" +
                "If Windows blocks ETW capture, consult official PresentMon documentation about Performance Log Users. " +
                "Do not disable antivirus, Roblox protections or Windows security.";
        }
    }

    // FeedCsvLine runs on the helper's output reader thread. No WPF calls here.
    private void FeedCsvLine(string line)
    {
        var fields = ParseCsvRow(line);
        if (fields.Count < 3) return;

        // A PresentMon capture starts with one CSV header. Find columns by name
        // rather than depending on a particular PresentMon metric ordering.
        int newPid = fields.FindIndex(x => x.Equals("ProcessID", StringComparison.OrdinalIgnoreCase));
        if (newPid >= 0)
        {
            int newInterval = fields.FindIndex(x => x.Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase));
            int newChain = fields.FindIndex(x => x.Equals("SwapChainAddress", StringComparison.OrdinalIgnoreCase));
            lock (_gate)
            {
                _pidColumn = newPid;
                _frameMsColumn = newInterval;
                _chainColumn = newChain;
                _status = newInterval >= 0
                    ? "Collecting measured render FPS..."
                    : "PresentMon CSV is missing MsBetweenPresents (unsupported format).";
            }
            return;
        }

        lock (_gate)
        {
            if (_pidColumn < 0 || _frameMsColumn < 0 ||
                Math.Max(_pidColumn, _frameMsColumn) >= fields.Count ||
                !int.TryParse(fields[_pidColumn], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int pid) ||
                !double.TryParse(fields[_frameMsColumn], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var intervalMs) ||
                !double.IsFinite(intervalMs) || intervalMs <= 0.02 || intervalMs > 1000)
                return;

            string chain = _chainColumn >= 0 && _chainColumn < fields.Count
                ? fields[_chainColumn] : "";
            var now = DateTime.UtcNow;
            if (!_frames.TryGetValue(pid, out var queue))
                _frames[pid] = queue = new List<Frame>(300);
            queue.Add(new Frame(chain, now, intervalMs));
            // Bound memory even for very high-FPS applications.
            if (queue.Count > 1600) queue.RemoveRange(0, queue.Count - 1600);
            queue.RemoveAll(f => (now - f.When).TotalSeconds > 2.5);
            _status = "Measuring render FPS using PresentMon";
        }
    }

    /// <summary>
    /// Return rolling application render FPS for an exact Roblox process ID.
    /// Avoid returning stale, zero, or made-up FPS while inactive.
    /// </summary>
    public double? GetRenderFps(int pid)
    {
        lock (_gate)
        {
            if (!_frames.TryGetValue(pid, out var frames) || frames.Count < 5)
                return null;

            DateTime now = DateTime.UtcNow;
            frames.RemoveAll(f => (now - f.When).TotalSeconds > 2.5);
            if (frames.Count < 5 || (now - frames[^1].When).TotalSeconds > 3)
                return null;

            // Multiple swap chains within one process must not be added together.
            var primary = frames.GroupBy(f => f.SwapChain)
                .OrderByDescending(g => g.Count()).First();
            int count = primary.Count();
            double sumMs = primary.Sum(f => f.IntervalMs);
            if (count < 5 || sumMs <= 0) return null;

            double fps = count * 1000.0 / sumMs;
            return fps > 0 && fps < 1000 ? fps : null;
        }
    }

    public void ForgetProcess(int pid)
    {
        lock (_gate) _frames.Remove(pid);
    }

    /// <summary>
    /// Pure parser check for CI: two PIDs with distinct 60 and 120 render FPS.
    /// Does not run ETW or need admin access.
    /// </summary>
    public static bool SmokeTestParser()
    {
        using var test = new RobloxFrameRateMonitor();
        test.FeedCsvLine("Application,ProcessID,SwapChainAddress,MsBetweenPresents");
        for (int i = 0; i < 40; i++)
        {
            test.FeedCsvLine("RobloxPlayerBeta.exe,101,0x1234,16.6667");
            test.FeedCsvLine("RobloxPlayerBeta.exe,202,0x5678,8.3333");
        }
        double? sixty = test.GetRenderFps(101);
        double? oneTwenty = test.GetRenderFps(202);
        return sixty is > 59 and < 61 && oneTwenty is > 119 and < 121 &&
               test.GetRenderFps(303) is null;
    }

    private static List<string> ParseCsvRow(string line)
    {
        var output = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else quoted = !quoted;
            }
            else if (ch == ',' && !quoted)
            {
                output.Add(current.ToString().Trim());
                current.Clear();
            }
            else current.Append(ch);
        }
        output.Add(current.ToString().Trim().TrimStart('\uFEFF'));
        return output;
    }

    private void Stop()
    {
        Process? old;
        lock (_gate)
        {
            old = _capture;
            _capture = null;
            _frames.Clear();
        }
        if (old == null) return;
        try { if (!old.HasExited) old.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally { old.Dispose(); }
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
