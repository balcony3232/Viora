using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace VioraManager;

public partial class MainWindow : Window
{
    private sealed record RunningWindow(int Pid, nint Handle, DateTime Started, long Memory)
    {
        public string Label { get; init; } = "";
        public string Detail => $"Roblox Player · {(DateTime.Now - Started):hh\\:mm} runtime · {Memory / (1024 * 1024)} MB";
    }

    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(3) };
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
        _refresh.Tick += (_, _) => RefreshWindows();
        Loaded += (_, _) => { RefreshWindows(); _refresh.Start(); };
        Closed += (_, _) => _refresh.Stop();
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

        _instances = result.OrderBy(x => x.Started).ThenBy(x => x.Pid)
            .Select((x, i) => x with { Label = $"Instance {i + 1:00}" }).ToList();
        InstanceCount.Text = _instances.Count.ToString();
        long total = _instances.Sum(x => x.Memory);
        MemoryText.Text = total >= 1073741824 ? $"{total / 1073741824d:0.0} GB" : $"{total / 1048576d:0} MB";
        StatusText.Text = _instances.Count > 0 ? "Active" : "Idle";
        InstanceList.ItemsSource = _instances;
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
