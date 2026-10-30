using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace VioraManager;

/// <summary>
/// Lightweight DWM live-window dashboard. Interactive mode activates the real
/// Roblox window on click; it does not inject or synthesize game input.
/// </summary>
public sealed class LiveInstanceViewer : Window
{
    private readonly WrapPanel _tiles = new() { Orientation = Orientation.Horizontal };
    private readonly ComboBox _mode = new() { Width = 155, Margin = new Thickness(8) };
    private readonly ComboBox _columns = new() { Width = 95, Margin = new Thickness(8) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<int, Preview> _previews = new();
    private readonly TextBlock _status = new() { Foreground = Brushes.LightGray, Margin = new Thickness(8) };
    private bool _interactive;

    private sealed class Preview
    {
        public int Pid { get; init; }
        public nint Source { get; init; }
        public nint Thumbnail { get; set; }
        public Border Tile { get; init; } = null!;
        public Border Surface { get; init; } = null!;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ThumbnailProperties
    {
        public uint Flags;
        public Rect Destination;
        public Rect Source;
        public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref ThumbnailProperties properties);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);

    public LiveInstanceViewer()
    {
        Title = "Viora Manager — Live Instance Viewer";
        Width = 1020; Height = 740; MinWidth = 540; MinHeight = 360;
        Background = new SolidColorBrush(Color.FromRgb(13, 9, 21));
        Foreground = Brushes.White;
        var root = new DockPanel { Margin = new Thickness(14) };
        Content = root;
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        bar.Children.Add(new TextBlock { Text = "LIVE INSTANCES", FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        _mode.Items.Add("Dashboard");
        _mode.Items.Add("Interactive");
        _mode.SelectedIndex = 0;
        _mode.SelectionChanged += (_, _) => { _interactive = _mode.SelectedIndex == 1; UpdateStatus(); };
        bar.Children.Add(_mode);
        foreach (var n in new[] { "Auto", "2 columns", "3 columns", "4 columns" }) _columns.Items.Add(n);
        _columns.SelectedIndex = 0;
        _columns.SelectionChanged += (_, _) => LayoutTiles();
        bar.Children.Add(_columns);
        var refresh = new Button { Content = "Refresh", Margin = new Thickness(8), Padding = new Thickness(10, 5, 10, 5) };
        refresh.Click += (_, _) => Refresh();
        bar.Children.Add(refresh);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _tiles };
        root.Children.Add(scroll);
        scroll.SizeChanged += (_, _) => LayoutTiles();
        _tiles.LayoutUpdated += (_, _) => UpdateThumbnails();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        _timer.Tick += (_, _) => Refresh();
        Closed += (_, _) => { _timer.Stop(); foreach (var p in _previews.Values) Release(p); _previews.Clear(); };
        UpdateStatus();
    }

    private void UpdateStatus() =>
        _status.Text = _interactive
            ? "Interactive: click a preview to focus its real Roblox window; keyboard and mouse input go to that window."
            : "Dashboard: live previews only. Minimized or protected windows may appear blank.";

    private void Refresh()
    {
        var found = new Dictionary<int, nint>();
        var processes = Process.GetProcessesByName("RobloxPlayerBeta");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited && process.MainWindowHandle != nint.Zero &&
                        IsWindow(process.MainWindowHandle) && IsWindowVisible(process.MainWindowHandle))
                        found[process.Id] = process.MainWindowHandle;
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        finally { foreach (var p in processes) p.Dispose(); }

        foreach (var old in _previews.Values.Where(p => !found.TryGetValue(p.Pid, out var h) || h != p.Source).ToArray())
        {
            Release(old);
            _tiles.Children.Remove(old.Tile);
            _previews.Remove(old.Pid);
        }
        foreach (var entry in found.OrderBy(x => x.Key))
        {
            if (_previews.ContainsKey(entry.Key)) continue;
            var surface = new Border { Background = Brushes.Black, Height = 190, Margin = new Thickness(7, 0, 7, 7) };
            var label = new TextBlock { Text = "Roblox · PID " + entry.Key, Foreground = Brushes.White, Margin = new Thickness(9), FontSize = 12 };
            var stack = new StackPanel();
            stack.Children.Add(label);
            stack.Children.Add(surface);
            var tile = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 21, 43)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(5),
                Child = stack
            };
            var source = entry.Value;
            tile.MouseLeftButtonUp += (_, _) =>
            {
                if (!_interactive || !IsWindow(source)) return;
                ShowWindow(source, 9);
                SetForegroundWindow(source);
            };
            var preview = new Preview { Pid = entry.Key, Source = source, Tile = tile, Surface = surface };
            _previews[entry.Key] = preview;
            _tiles.Children.Add(tile);
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != nint.Zero && DwmRegisterThumbnail(hwnd, source, out var thumb) == 0)
                preview.Thumbnail = thumb;
        }
        LayoutTiles();
    }

    private void LayoutTiles()
    {
        if (_tiles.Children.Count == 0) return;
        var available = Math.Max(360, ActualWidth - 65);
        int cols = _columns.SelectedIndex switch { 1 => 2, 2 => 3, 3 => 4, _ => available > 950 ? 3 : available > 620 ? 2 : 1 };
        var width = Math.Max(155, (available / cols) - 12);
        foreach (var p in _previews.Values)
        {
            p.Tile.Width = width;
            p.Surface.Height = Math.Max(105, width * 9 / 16);
        }
        Dispatcher.BeginInvoke(new Action(UpdateThumbnails), DispatcherPriority.Loaded);
    }

    private void UpdateThumbnails()
    {
        if (!IsLoaded || WindowState == WindowState.Minimized) return;
        foreach (var p in _previews.Values)
        {
            if (p.Thumbnail == nint.Zero || !p.Surface.IsVisible || p.Surface.ActualWidth < 1) continue;
            try
            {
                var topLeft = p.Surface.PointToScreen(new Point(0, 0));
                var bottomRight = p.Surface.PointToScreen(new Point(p.Surface.ActualWidth, p.Surface.ActualHeight));
                var origin = PointToScreen(new Point(0, 0));
                var props = new ThumbnailProperties
                {
                    Flags = 0x1 | 0x4 | 0x8,
                    Destination = new Rect
                    {
                        Left = (int)(topLeft.X - origin.X),
                        Top = (int)(topLeft.Y - origin.Y),
                        Right = (int)(bottomRight.X - origin.X),
                        Bottom = (int)(bottomRight.Y - origin.Y)
                    },
                    Opacity = 255,
                    Visible = true
                };
                _ = DwmUpdateThumbnailProperties(p.Thumbnail, ref props);
            }
            catch (InvalidOperationException) { }
        }
    }

    private static void Release(Preview preview)
    {
        if (preview.Thumbnail != nint.Zero) _ = DwmUnregisterThumbnail(preview.Thumbnail);
        preview.Thumbnail = nint.Zero;
    }
}
