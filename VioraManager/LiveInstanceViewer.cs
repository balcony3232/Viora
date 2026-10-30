using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace VioraManager;

/// <summary>
/// DWM live previews of official Roblox windows. Interactive input forwarding
/// uses ordinary Win32 window messages, never injection, hooks, credentials or
/// Roblox client modification. Games using Raw Input may ignore these messages.
/// </summary>
public sealed class LiveInstanceViewer : Window
{
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_CHAR = 0x0102;
    private const int MK_LBUTTON = 0x0001;
    private const int MK_RBUTTON = 0x0002;
    private const int DWM_DESTINATION = 0x0001;
    private const int DWM_OPACITY = 0x0004;
    private const int DWM_VISIBLE = 0x0008;
    private const int DWM_CLIENT_ONLY = 0x0010;

    private readonly Func<int, string> _getGameName;
    private readonly WrapPanel _tiles = new();
    private readonly ScrollViewer _scroll;
    private readonly ComboBox _mode = new() { Width = 190, Margin = new Thickness(8, 0, 8, 0) };
    private readonly ComboBox _columns = new() { Width = 130, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _summary = new() { Foreground = new SolidColorBrush(Color.FromRgb(205, 180, 233)), Margin = new Thickness(0, 10, 0, 3), FontSize = 12 };
    private readonly TextBlock _status = new() { Foreground = new SolidColorBrush(Color.FromRgb(185, 166, 205)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontSize = 11 };
    private readonly TextBlock _empty = new() { Text = "No Roblox windows found. Launch Roblox and click Refresh.", Foreground = Brushes.LightGray, FontSize = 14, Margin = new Thickness(14) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<int, Preview> _previews = new();
    private readonly Dictionary<int, CpuSample> _cpuSamples = new();
    private bool _interactive;
    private bool _leftDown;
    private bool _rightDown;
    private int? _selectedPid;

    private sealed record CpuSample(DateTime SampledUtc, TimeSpan TotalCpu);
    private sealed record ProcessSnapshot(int Pid, nint Hwnd, DateTime Started,
        long Memory, double CpuPercent);

    private sealed class Preview
    {
        public int Pid { get; init; }
        public nint Source { get; init; }
        public nint Thumbnail { get; set; }
        public string Label { get; set; } = "";
        public Border Tile { get; init; } = null!;
        public Border Surface { get; init; } = null!;
        public TextBlock Header { get; init; } = null!;
        public TextBlock Game { get; init; } = null!;
        public TextBlock Metrics { get; init; } = null!;
        public TextBox LabelInput { get; init; } = null!;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint { public int X, Y; }
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref ThumbnailProperties properties);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref ScreenPoint point);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    public LiveInstanceViewer(Func<int, string> getGameName)
    {
        _getGameName = getGameName;
        Title = "Viora Manager — Live Instance Viewer";
        Width = 1120;
        Height = 790;
        MinWidth = 550;
        MinHeight = 390;
        Background = new SolidColorBrush(Color.FromRgb(13, 9, 21));
        Foreground = Brushes.White;

        var root = new DockPanel { Margin = new Thickness(18) };
        Content = root;
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var bar = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        top.Children.Add(bar);
        bar.Children.Add(new TextBlock
        {
            Text = "LIVE INSTANCES", FontSize = 17, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0)
        });

        _mode.Items.Add("Dashboard");
        _mode.Items.Add("Interactive (test)");
        _mode.SelectedIndex = 0;
        _mode.SelectionChanged += (_, _) =>
        {
            _interactive = _mode.SelectedIndex == 1;
            _selectedPid = null;
            _leftDown = false;
            _rightDown = false;
            UpdateSelection();
            UpdateStatus();
        };
        bar.Children.Add(_mode);

        foreach (var option in new[] { "Auto grid", "1 column", "2 columns", "3 columns", "4 columns" })
            _columns.Items.Add(option);
        _columns.SelectedIndex = 0;
        _columns.SelectionChanged += (_, _) => LayoutTiles();
        bar.Children.Add(_columns);

        var refresh = MakeButton("Refresh");
        refresh.Click += (_, _) => Refresh();
        bar.Children.Add(refresh);
        top.Children.Add(_summary);

        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel();
        panel.Children.Add(_empty);
        panel.Children.Add(_tiles);
        _scroll.Content = panel;
        root.Children.Add(_scroll);
        _scroll.SizeChanged += (_, _) => LayoutTiles();
        _scroll.ScrollChanged += (_, _) => UpdateThumbnails();
        _tiles.LayoutUpdated += (_, _) => UpdateThumbnails();

        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        _timer.Tick += (_, _) => Refresh();
        Closed += (_, _) =>
        {
            _timer.Stop();
            foreach (var preview in _previews.Values) Release(preview);
            _previews.Clear();
        };
        UpdateStatus();
    }

    private static Button MakeButton(string caption) =>
        new() { Content = caption, Padding = new Thickness(11, 7, 11, 7), Margin = new Thickness(4, 0, 4, 0), FontSize = 12 };

    private static TextBlock MakeDetail(string text, double size = 12) =>
        new() { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(194, 171, 220)),
            FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };

    private void UpdateStatus()
    {
        _status.Text = _interactive
            ? "Interactive (experimental): click a preview and try mouse/keyboard controls inside it. Roblox may reject background input (especially movement/camera). Use Focus game if it does; Esc exits preview input focus."
            : "Dashboard: previews are live and read-only. Account labels are manual session labels, not verified Roblox usernames. Actual in-game FPS is not available without client instrumentation.";
    }

    private void Refresh()
    {
        var found = new Dictionary<int, ProcessSnapshot>();
        var processes = Process.GetProcessesByName("RobloxPlayerBeta");
        var now = DateTime.UtcNow;
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    process.Refresh();
                    if (process.HasExited || process.MainWindowHandle == nint.Zero ||
                        !IsWindow(process.MainWindowHandle) || !IsWindowVisible(process.MainWindowHandle))
                        continue;

                    var total = process.TotalProcessorTime;
                    var cpu = 0.0;
                    if (_cpuSamples.TryGetValue(process.Id, out var last))
                    {
                        var elapsed = (now - last.SampledUtc).TotalMilliseconds;
                        if (elapsed > 0)
                            cpu = Math.Clamp((total - last.TotalCpu).TotalMilliseconds /
                                elapsed / Math.Max(1, Environment.ProcessorCount) * 100, 0, 100);
                    }
                    _cpuSamples[process.Id] = new CpuSample(now, total);
                    found[process.Id] = new ProcessSnapshot(process.Id, process.MainWindowHandle,
                        process.StartTime, process.WorkingSet64, cpu);
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }

        foreach (var old in _previews.Values.Where(x => !found.TryGetValue(x.Pid, out var value)
                 || value.Hwnd != x.Source).ToArray())
        {
            Release(old);
            _tiles.Children.Remove(old.Tile);
            _previews.Remove(old.Pid);
            if (_selectedPid == old.Pid) _selectedPid = null;
        }
        foreach (var pid in _cpuSamples.Keys.Where(pid => !found.ContainsKey(pid)).ToArray())
            _cpuSamples.Remove(pid);

        int index = 0;
        foreach (var snapshot in found.Values.OrderBy(x => x.Started).ThenBy(x => x.Pid))
        {
            index++;
            if (!_previews.TryGetValue(snapshot.Pid, out var preview))
            {
                preview = CreatePreview(snapshot);
                _previews.Add(snapshot.Pid, preview);
                _tiles.Children.Add(preview.Tile);
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != nint.Zero && DwmRegisterThumbnail(hwnd, snapshot.Hwnd, out var thumb) == 0)
                    preview.Thumbnail = thumb;
            }
            preview.Header.Text = string.IsNullOrWhiteSpace(preview.Label)
                ? "Instance " + index.ToString("00") : preview.Label;
            try { preview.Game.Text = _getGameName(snapshot.Pid); }
            catch (InvalidOperationException) { preview.Game.Text = "Game not detected"; }
            var minutes = Math.Max(0, (int)(DateTime.Now - snapshot.Started).TotalMinutes);
            preview.Metrics.Text = "PID " + snapshot.Pid + "  ·  RAM " +
                (snapshot.Memory / 1048576d).ToString("0") + " MB  ·  CPU " +
                snapshot.CpuPercent.ToString("0.0") + "%  ·  " + minutes +
                "m runtime  ·  Game FPS: n/a";
        }
        _empty.Visibility = found.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _summary.Text = found.Count + (found.Count == 1
            ? " Roblox window detected" : " Roblox windows detected") +
            "   ·   " + ( _interactive ? "Interactive test" : "Live dashboard");
        UpdateSelection();
        LayoutTiles();
    }

    private Preview CreatePreview(ProcessSnapshot snapshot)
    {
        var heading = new TextBlock
        {
            Foreground = Brushes.White, FontSize = 15,
            FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis
        };
        var game = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(193, 156, 251)),
            FontSize = 12, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 5, 0, 8)
        };
        var metrics = MakeDetail("", 11);
        metrics.Margin = new Thickness(0, 7, 0, 6);
        var surface = new Border
        {
            Height = 260, Background = Brushes.Black, Focusable = true,
            Cursor = Cursors.Hand, BorderBrush = new SolidColorBrush(Color.FromRgb(71, 49, 95)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5)
        };
        var account = new TextBox { Width = 170, MaxLength = 32, Margin = new Thickness(0, 4, 7, 3),
            ToolTip = "Optional manual account label for this running instance; no Roblox sign-in data is read." };
        var save = MakeButton("Set label");
        var focus = MakeButton("Focus game");
        focus.Click += (_, _) => FocusGame(snapshot.Hwnd);
        var accountRow = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        accountRow.Children.Add(account);
        accountRow.Children.Add(save);
        accountRow.Children.Add(focus);
        var stack = new StackPanel { Margin = new Thickness(12) };
        stack.Children.Add(heading);
        stack.Children.Add(game);
        stack.Children.Add(surface);
        stack.Children.Add(metrics);
        stack.Children.Add(accountRow);
        var tile = new Border
        {
            Child = stack, Margin = new Thickness(6),
            Background = new SolidColorBrush(Color.FromRgb(28, 20, 40)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(68, 46, 91)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11)
        };
        var preview = new Preview { Pid = snapshot.Pid, Source = snapshot.Hwnd, Header = heading,
            Game = game, Metrics = metrics, Surface = surface, Tile = tile, LabelInput = account };
        save.Click += (_, _) =>
        {
            preview.Label = account.Text.Trim();
            preview.Header.Text = string.IsNullOrEmpty(preview.Label)
                ? "Instance " + snapshot.Pid : preview.Label;
        };
        account.KeyDown += (_, e) => { if (e.Key == Key.Enter) { save.RaiseEvent(
            new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; } };

        surface.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!_interactive) return;
            SelectPreview(preview);
            Keyboard.Focus(surface);
            _leftDown = true;
            SendMouse(preview, WM_LBUTTONDOWN, MK_LBUTTON, e.GetPosition(surface));
            Mouse.Capture(surface);
            e.Handled = true;
        };
        surface.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!_interactive || _selectedPid != preview.Pid) return;
            SendMouse(preview, WM_LBUTTONUP, 0, e.GetPosition(surface));
            _leftDown = false;
            if (!_rightDown) Mouse.Capture(null);
            e.Handled = true;
        };
        surface.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (!_interactive) return;
            SelectPreview(preview);
            Keyboard.Focus(surface);
            _rightDown = true;
            SendMouse(preview, WM_RBUTTONDOWN, MK_RBUTTON, e.GetPosition(surface));
            Mouse.Capture(surface);
            e.Handled = true;
        };
        surface.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (!_interactive || _selectedPid != preview.Pid) return;
            SendMouse(preview, WM_RBUTTONUP, 0, e.GetPosition(surface));
            _rightDown = false;
            if (!_leftDown) Mouse.Capture(null);
            e.Handled = true;
        };
        surface.MouseMove += (_, e) =>
        {
            if (_interactive && _selectedPid == preview.Pid)
                SendMouse(preview, WM_MOUSEMOVE,
                    (_leftDown ? MK_LBUTTON : 0) | (_rightDown ? MK_RBUTTON : 0),
                    e.GetPosition(surface));
        };
        surface.PreviewMouseWheel += (_, e) =>
        {
            if (!_interactive || _selectedPid != preview.Pid) return;
            if (TryClientPoint(preview, e.GetPosition(surface), out var client))
            {
                var screen = client;
                if (ClientToScreen(preview.Source, ref screen))
                {
                    PostMessage(preview.Source, WM_MOUSEWHEEL,
                        (nint)unchecked(e.Delta << 16), PackPoint(screen.X, screen.Y));
                    e.Handled = true;
                }
            }
        };
        surface.PreviewKeyDown += (_, e) =>
        {
            if (!_interactive || _selectedPid != preview.Pid) return;
            if (e.Key == Key.Escape)
            {
                _selectedPid = null; UpdateSelection(); e.Handled = true; return;
            }
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk != 0 && IsWindow(preview.Source))
            {
                PostMessage(preview.Source, WM_KEYDOWN, (nint)vk, 0);
                e.Handled = true;
            }
        };
        surface.PreviewKeyUp += (_, e) =>
        {
            if (!_interactive || _selectedPid != preview.Pid) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk != 0 && IsWindow(preview.Source))
            {
                PostMessage(preview.Source, WM_KEYUP, (nint)vk, 0);
                e.Handled = true;
            }
        };
        surface.PreviewTextInput += (_, e) =>
        {
            if (!_interactive || _selectedPid != preview.Pid || !IsWindow(preview.Source)) return;
            foreach (char character in e.Text)
                PostMessage(preview.Source, WM_CHAR, (nint)(int)character, 0);
            e.Handled = true;
        };
        return preview;
    }

    private void SelectPreview(Preview preview)
    {
        _selectedPid = preview.Pid;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        foreach (var item in _previews.Values)
            item.Tile.BorderBrush = _interactive && item.Pid == _selectedPid
                ? new SolidColorBrush(Color.FromRgb(181, 125, 251))
                : new SolidColorBrush(Color.FromRgb(68, 46, 91));
    }

    private static void FocusGame(nint handle)
    {
        if (!IsWindow(handle)) return;
        ShowWindow(handle, 9);
        SetForegroundWindow(handle);
    }

    private bool TryClientPoint(Preview preview, Point local, out ScreenPoint point)
    {
        point = default;
        if (!IsWindow(preview.Source) || !GetClientRect(preview.Source, out var rect) ||
            preview.Surface.ActualWidth < 2 || preview.Surface.ActualHeight < 2)
            return false;
        int w = Math.Max(1, rect.Right - rect.Left);
        int h = Math.Max(1, rect.Bottom - rect.Top);
        point = new ScreenPoint
        {
            X = Math.Clamp((int)(local.X / preview.Surface.ActualWidth * w), 0, w - 1),
            Y = Math.Clamp((int)(local.Y / preview.Surface.ActualHeight * h), 0, h - 1)
        };
        return true;
    }

    private static nint PackPoint(int x, int y) => (nint)unchecked((y & 0xffff) << 16 | (x & 0xffff));

    private void SendMouse(Preview preview, uint message, int flags, Point local)
    {
        if (!TryClientPoint(preview, local, out var point)) return;
        PostMessage(preview.Source, message, (nint)flags, PackPoint(point.X, point.Y));
    }

    private void LayoutTiles()
    {
        if (_previews.Count == 0 || !IsLoaded) return;
        var available = Math.Max(350, _scroll.ViewportWidth > 10 ? _scroll.ViewportWidth : ActualWidth - 60);
        int auto = available >= 1000 ? 3 : available >= 730 ? 2 : 1;
        int requested = _columns.SelectedIndex switch { 1 => 1, 2 => 2, 3 => 3, 4 => 4, _ => auto };
        int cols = Math.Max(1, Math.Min(_previews.Count, requested));
        double width = Math.Max(170, (available / cols) - 14);
        foreach (var preview in _previews.Values)
        {
            if (Math.Abs(preview.Tile.Width - width) > 0.5 || double.IsNaN(preview.Tile.Width))
                preview.Tile.Width = width;
            double height = Math.Max(120, (width - 26) * 9 / 16);
            if (Math.Abs(preview.Surface.Height - height) > 0.5)
                preview.Surface.Height = height;
        }
        Dispatcher.BeginInvoke(new Action(UpdateThumbnails), DispatcherPriority.Loaded);
    }

    private void UpdateThumbnails()
    {
        if (!IsLoaded || WindowState == WindowState.Minimized) return;
        foreach (var preview in _previews.Values)
        {
            if (preview.Thumbnail == nint.Zero) continue;
            bool visible = preview.Surface.IsVisible && preview.Surface.ActualWidth > 1;
            if (visible)
            {
                var offset = preview.Surface.TranslatePoint(new Point(0, 0), _scroll);
                visible = offset.Y >= 0 && offset.Y + preview.Surface.ActualHeight <= _scroll.ViewportHeight;
            }
            var props = new ThumbnailProperties
            {
                Flags = DWM_DESTINATION | DWM_OPACITY | DWM_VISIBLE | DWM_CLIENT_ONLY,
                Opacity = 255, Visible = visible, SourceClientAreaOnly = true
            };
            if (visible)
            {
                var origin = PointToScreen(new Point(0, 0));
                var topLeft = preview.Surface.PointToScreen(new Point(0, 0));
                var bottomRight = preview.Surface.PointToScreen(
                    new Point(preview.Surface.ActualWidth, preview.Surface.ActualHeight));
                props.Destination = new Rect
                {
                    Left = (int)(topLeft.X - origin.X),
                    Top = (int)(topLeft.Y - origin.Y),
                    Right = (int)(bottomRight.X - origin.X),
                    Bottom = (int)(bottomRight.Y - origin.Y)
                };
            }
            _ = DwmUpdateThumbnailProperties(preview.Thumbnail, ref props);
        }
    }

    private static void Release(Preview preview)
    {
        if (preview.Thumbnail != nint.Zero)
            _ = DwmUnregisterThumbnail(preview.Thumbnail);
        preview.Thumbnail = nint.Zero;
    }
}
