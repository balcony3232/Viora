using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Threading;

namespace VioraManager;

public partial class MainWindow : Window
{
    private sealed record RunningWindow(int Pid, nint Handle, DateTime Started, long Memory)
    {
        public string Label { get; init; } = "";
        public string GameName { get; init; } = "Game not detected";
        public string Detail => $"{Math.Max(0, (int)(DateTime.Now - Started).TotalMinutes)}m runtime · {Memory / (1024 * 1024)} MB RAM";
    }

    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly MultiInstanceExperiment _experiment = new();
    private readonly RobloxInstanceGames _games;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    private void ApplyDarkTitleBar()
    {
        // Keep the Windows chrome in the same dark theme as the Manager surface.
        try
        {
            int enabled = 1;
            var window = new WindowInteropHelper(this).Handle;
            if (DwmSetWindowAttribute(window, 20, ref enabled, sizeof(int)) != 0)
                _ = DwmSetWindowAttribute(window, 19, ref enabled, sizeof(int));
        }
        catch (DllNotFoundException) { } // Old Windows build: retain system title bar.
        catch (EntryPointNotFoundException) { }
    }
    private List<RunningWindow> _instances = new();

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    private const int RestoreWindow = 9;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;

    public MainWindow()
    {
        InitializeComponent();
        _games = new RobloxInstanceGames(() => Dispatcher.BeginInvoke(new Action(RefreshWindows)));
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        _refresh.Tick += (_, _) => RefreshWindows();
        Loaded += (_, _) => { RefreshWindows(); _refresh.Start(); };
        Closed += (_, _) => { _refresh.Stop(); _experiment.Disable(); };
    }

    private void RefreshWindows()
    {
        var result = new List<RunningWindow>();
        var processes = Process.GetProcessesByName("RobloxPlayerBeta");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    process.Refresh();
                    if (process.HasExited || process.MainWindowHandle == nint.Zero ||
                        !IsWindowVisible(process.MainWindowHandle)) continue;
                    result.Add(new RunningWindow(process.Id, process.MainWindowHandle,
                        process.StartTime, process.WorkingSet64));
                }
                catch (InvalidOperationException) { } // Process closed during sample
                catch (System.ComponentModel.Win32Exception) { } // Unreadable process
            }
        }
        finally { foreach (var p in processes) p.Dispose(); }

        _games.Observe(result.Select(x =>
            new RobloxInstanceGames.RunningProcess(x.Pid, x.Started.ToUniversalTime())).ToArray());

        _instances = result.OrderBy(x => x.Started).ThenBy(x => x.Pid)
            .Select((x, i) => x with
            {
                Label = $"Instance {i + 1:00}",
                GameName = _games.GetGameName(x.Pid)
            }).ToList();
        InstanceCount.Text = _instances.Count.ToString();
        long total = _instances.Sum(x => x.Memory);
        MemoryText.Text = total >= 1073741824 ? $"{total / 1073741824d:0.0} GB" : $"{total / 1048576d:0} MB";
        StatusText.Text = _instances.Count > 0 ? "Active" : "Idle";
        InstanceList.ItemsSource = _instances;
    }

    private void MultiInstanceToggle_Click(object sender, RoutedEventArgs e)
    {
        if (MultiInstanceToggle.IsChecked != true)
        {
            _experiment.Disable();
            ExperimentStatus.Text = "Experimental mode disabled. Any already-running Roblox clients are unaffected.";
            return;
        }

        bool robloxRunning = false;
        var processes = Process.GetProcessesByName("RobloxPlayerBeta");
        try { robloxRunning = processes.Any(p => { try { return !p.HasExited; } catch { return false; } }); }
        finally { foreach (var process in processes) process.Dispose(); }

        if (robloxRunning)
        {
            MultiInstanceToggle.IsChecked = false;
            ExperimentStatus.Text = "Close all Roblox Player clients before enabling this mode.";
            MessageBox.Show(this,
                "Close all Roblox Player windows before enabling the experiment. Then enable it and launch the first Roblox game through the official website.",
                "Start the experiment before Roblox", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(this,
            "This experimental setting holds a legacy Windows mutex used for Roblox's single-instance behaviour. It does not edit Roblox files, inject code, or access account credentials.\n\n" +
            "Roblox does not officially support simultaneous Player clients. Behaviour can break after updates, and Roblox's rules can change. No guarantee of account safety or compatibility is offered.\n\n" +
            "This test remains enabled only while Viora Manager stays open. Enable it now?",
            "Experimental multi-instance mode",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            MultiInstanceToggle.IsChecked = false;
            ExperimentStatus.Text = "Experimental mode not enabled.";
            return;
        }
        if (_experiment.TryEnable(out string message))
        {
            ExperimentStatus.Text = message;
        }
        else
        {
            MultiInstanceToggle.IsChecked = false;
            ExperimentStatus.Text = message;
            MessageBox.Show(this, message, "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshWindows();

    private void OpenRoblox_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Open the official site; never attempt to bypass Roblox's single-instance rules.
            Process.Start(new ProcessStartInfo("https://www.roblox.com/home") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Unable to open Roblox: " + ex.Message, "Viora Manager",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ManageWindows_Click(object sender, RoutedEventArgs e)
    {
        RefreshWindows();
        if (_instances.Count == 0)
        {
            MessageBox.Show(this, "No visible official Roblox Player windows were detected.", "Viora Manager");
            return;
        }
        if (_instances.Count == 1)
        {
            ShowWindow(_instances[0].Handle, RestoreWindow);
            SetForegroundWindow(_instances[0].Handle);
            return;
        }
        if (MessageBox.Show(this,
            $"Arrange {_instances.Count} existing Roblox Player windows across your desktop?\n\n" +
            "This only changes window positions; it does not launch or modify Roblox.",
            "Arrange windows", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var area = SystemParameters.WorkArea;
        int columns = (int)Math.Ceiling(Math.Sqrt(_instances.Count));
        int rows = (int)Math.Ceiling(_instances.Count / (double)columns);
        int width = Math.Max(340, (int)(area.Width / columns));
        int height = Math.Max(260, (int)(area.Height / rows));
        for (int i = 0; i < _instances.Count; i++)
        {
            var instance = _instances[i];
            if (!IsWindowVisible(instance.Handle)) continue;
            ShowWindow(instance.Handle, RestoreWindow);
            int left = (int)area.Left + (i % columns) * width;
            int top = (int)area.Top + (i / columns) * height;
            _ = SetWindowPos(instance.Handle, nint.Zero, left, top, width, height, NoZOrder | NoActivate);
        }
    }
}
