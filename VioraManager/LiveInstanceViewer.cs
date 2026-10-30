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
/// DWM thumbnails are display-only. Interactive mode switches focus to each
/// actual Roblox window for native keyboard/mouse input; no synthetic messages,
/// injection, hooks, credentials or Roblox client modification.
/// </summary>
public sealed class LiveInstanceViewer : Window
{
    private const int DWM_DESTINATION = 0x0001;
    private const int DWM_OPACITY = 0x0004;
    private const int DWM_VISIBLE = 0x0008;
    private const int DWM_CLIENT_ONLY = 0x0010;

    private readonly Func<int, string> _getGameName;
    private readonly Action? _tilePlayableWindows;
    private readonly RobloxFrameRateMonitor _fps = new();
    private readonly WrapPanel _tiles = new();
    private readonly ScrollViewer _scroll;
    // Direct actions avoid the unreliable custom ComboBox popup template.
    private readonly Button _dashboardButton = MakeButton("Dashboard");
    private readonly Button _interactiveButton = MakeButton("Interactive");
    private readonly Button _layoutButton = MakeButton("Grid: Auto");
    private readonly string[] _layoutNames = { "Auto", "1 column", "2 columns", "3 columns", "4 columns" };
    private int _layoutIndex;
    private readonly TextBlock _summary = new() { Foreground = new SolidColorBrush(Color.FromRgb(205, 180, 233)), Margin = new Thickness(0, 10, 0, 3), FontSize = 12 };
    private readonly TextBlock _status = new() { Foreground = new SolidColorBrush(Color.FromRgb(185, 166, 205)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontSize = 11 };
    private readonly TextBlock _empty = new() { Text = "No Roblox windows found. Launch Roblox and click Refresh.", Foreground = Brushes.LightGray, FontSize = 14, Margin = new Thickness(14) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<int, Preview> _previews = new();
    private readonly Dictionary<int, CpuSample> _cpuSamples = new();
    private bool _interactive;
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
    private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    public LiveInstanceViewer(Func<int, string> getGameName, Action? tilePlayableWindows = null)
    {
        _getGameName = getGameName;
        _tilePlayableWindows = tilePlayableWindows;
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

        _dashboardButton.Click += (_, _) => SetInteractiveMode(false);
        _interactiveButton.Click += (_, _) => SetInteractiveMode(true);
        _layoutButton.Click += (_, _) =>
        {
            _layoutIndex = (_layoutIndex + 1) % _layoutNames.Length;
            _layoutButton.Content = "Grid: " + _layoutNames[_layoutIndex];
            LayoutTiles();
        };
        _layoutButton.ToolTip = "Click repeatedly to choose Auto, 1, 2, 3, or 4 columns.";
        bar.Children.Add(_dashboardButton);
        bar.Children.Add(_interactiveButton);
        bar.Children.Add(_layoutButton);
        var tileAndPlay = MakeButton("Tile & play windows");
        tileAndPlay.ToolTip = "Arrange real Roblox windows side by side, then play normally; the previews remain display-only.";
        tileAndPlay.Click += (_, _) =>
        {
            Refresh();
            if (_previews.Count == 0)
            {
                MessageBox.Show(this, "Open Roblox before arranging playable windows.",
                    "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (_tilePlayableWindows == null)
            {
                MessageBox.Show(this, "Desktop arrangement is unavailable in this preview context.",
                    "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var answer = MessageBox.Show(this,
                "Arrange your existing Roblox windows on the desktop and open the Mini-manager? " +
                "You can play normally in the real game windows. Their positions will change; no clients will be launched.",
                "Tile playable Roblox windows", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            WindowState = WindowState.Minimized;
            _tilePlayableWindows();
        };
        bar.Children.Add(tileAndPlay);
        UpdateModeButtons();

        var refresh = MakeButton("Refresh");
        refresh.Click += (_, _) => Refresh();
        bar.Children.Add(refresh);
        var restartFps = MakeButton("Restart FPS");
        restartFps.ToolTip = "Restart the optional Intel PresentMon process-based FPS sampler.";
        restartFps.Click += (_, _) => { _fps.Start(); Refresh(); };
        bar.Children.Add(restartFps);
        var fpsDetails = MakeButton("FPS details");
        fpsDetails.ToolTip = "Show the helper's exit code and last error so FPS failures can be diagnosed.";
        fpsDetails.Click += (_, _) =>
        {
            string details = _fps.DiagnosticSummary();
            var choice = MessageBox.Show(this,
                details + "\n\nCopy these FPS diagnostics to clipboard?",
                "Viora Manager — FPS diagnostics",
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (choice == MessageBoxResult.Yes)
            {
                try { Clipboard.SetText(details); }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    MessageBox.Show(this, "Windows clipboard is unavailable right now.",
                        "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        };
        bar.Children.Add(fpsDetails);
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

        Loaded += (_, _) => { _fps.Start(); Refresh(); _timer.Start(); };
        _timer.Tick += (_, _) => Refresh();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _fps.Dispose();
            foreach (var preview in _previews.Values) Release(preview);
            _previews.Clear();
        };
        UpdateStatus();
    }

    // Validate clickable mode and grid controls in the packaged Windows smoke test.
    internal bool SmokeTestModeButtons()
    {
        _interactiveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        bool interactiveSelected = _interactive && _selectedPid == null;
        _dashboardButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        bool dashboardSelected = !_interactive;
        _layoutButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        bool layoutAdvanced = _layoutIndex == 1;
        return interactiveSelected && dashboardSelected && layoutAdvanced;
    }

    private static Button MakeButton(string caption) =>
        new() { Content = caption, Padding = new Thickness(11, 7, 11, 7), Margin = new Thickness(4, 0, 4, 0), FontSize = 12 };

    private void SetInteractiveMode(bool interactive)
    {
        if (_interactive == interactive) return;
        // Release input capture before switching modes, so the mouse remains usable.
        if (Mouse.Captured != null) Mouse.Capture(null);
        _interactive = interactive;
        _selectedPid = null;
        UpdateModeButtons();
        UpdateSelection();
        UpdateStatus();
    }

    private void UpdateModeButtons()
    {
        var active = new SolidColorBrush(Color.FromRgb(161, 111, 236));
        var inactive = new SolidColorBrush(Color.FromRgb(55, 38, 76));
        _dashboardButton.Background = !_interactive ? active : inactive;
        _interactiveButton.Background = _interactive ? active : inactive;
        _dashboardButton.FontWeight = !_interactive ? FontWeights.SemiBold : FontWeights.Normal;
        _interactiveButton.FontWeight = _interactive ? FontWeights.SemiBold : FontWeights.Normal;
        _dashboardButton.ToolTip = !_interactive ? "Dashboard mode is selected" : "Switch to read-only previews";
        _interactiveButton.ToolTip = _interactive ? "Click a preview to focus the real Roblox game window" : "Activate a real Roblox window by clicking its preview";
    }

    private static TextBlock MakeDetail(string text, double size = 12) =>
        new() { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(194, 171, 220)),
            FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };

    private void UpdateStatus()
    {
        var modeStatus = _interactive
            ? "Interactive: click a preview to activate its REAL Roblox window and play normally there. For multiple accounts, choose Tile & play windows. Previews cannot receive native game input."
            : "Dashboard: live read-only previews. Account labels are manual and not verified Roblox usernames.";
        _status.Text = modeStatus + "   ·   Render FPS: " + _fps.Status;
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
            _fps.ForgetProcess(old.Pid);
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
            var renderFps = _fps.GetRenderFps(snapshot.Pid);
            string fpsValue = renderFps.HasValue
                ? renderFps.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                : "—";
            preview.Metrics.Text = "PID " + snapshot.Pid + "  ·  RAM " +
                (snapshot.Memory / 1048576d).ToString("0") + " MB  ·  CPU " +
                snapshot.CpuPercent.ToString("0.0") + "%  ·  " + minutes +
                "m runtime  ·  Render FPS: " + fpsValue;
            preview.Metrics.ToolTip = renderFps.HasValue
                ? "Measured Roblox application frame presents per second (Intel PresentMon ETW). Not necessarily displayed FPS."
                : "Render FPS not available yet. See the status below for capture errors.";
        }
        _empty.Visibility = found.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _summary.Text = found.Count + (found.Count == 1
            ? " Roblox window detected" : " Roblox windows detected") +
            "   ·   " + (_interactive ? "Native window control" : "Live dashboard");
        UpdateSelection();
        UpdateStatus();
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
            // A DWM thumbnail is a picture, not a native input target.
            // Activate the original HWND instead of sending synthetic messages
            // which Roblox's Raw Input path may ignore.
            FocusGame(preview.Source);
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

    private void LayoutTiles()
    {
        if (_previews.Count == 0 || !IsLoaded) return;
        var available = Math.Max(350, _scroll.ViewportWidth > 10 ? _scroll.ViewportWidth : ActualWidth - 60);
        int auto = available >= 1000 ? 3 : available >= 730 ? 2 : 1;
        int requested = _layoutIndex switch { 1 => 1, 2 => 2, 3 => 3, 4 => 4, _ => auto };
        int cols = Math.Max(1, Math.Min(_previews.Count, requested));
        // Keep a single preview large but still completely visible in the
        // scrolling viewport, including its header, stats and label controls.
        // DWM thumbnails are native overlays and cannot use WPF's ScrollViewer clip.
        double verticalSpace = Math.Max(220, _scroll.ViewportHeight - 170);
        double widthCap = verticalSpace * 16 / 9 + 26;
        double width = Math.Max(170, Math.Min((available / cols) - 14, widthCap));
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
