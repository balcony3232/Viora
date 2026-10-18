using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VioraManager;

// Manager 2.0 controls only visible official Player windows. No force-killing,
// code injection, account access, or hidden-process control is performed.
public partial class MainWindow
{
    private sealed record CpuReading(DateTime Started, TimeSpan Total, DateTime SampleUtc);
    private readonly Dictionary<int, CpuReading> _cpuSamples = new();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsHungAppWindow(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint handle, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const uint CloseMessage = 0x0010;
    private const int MinimizeWindow = 6;

    private double SampleCpu(int pid, DateTime start, TimeSpan usage)
    {
        var now = DateTime.UtcNow;
        double percent = 0;
        if (_cpuSamples.TryGetValue(pid, out var previous) && previous.Started == start)
        {
            double elapsed = (now - previous.SampleUtc).TotalSeconds;
            double used = (usage - previous.Total).TotalSeconds;
            if (elapsed > 0 && used >= 0)
                percent = Math.Clamp(used / (elapsed * Environment.ProcessorCount) * 100d, 0, 100);
        }
        _cpuSamples[pid] = new CpuReading(start, usage, now);
        return percent;
    }

    // PID reuse and stale window handles are checked immediately before every
    // action: a card cannot accidentally target a newly launched process.
    private RunningWindow? ResolveWindow(int pid)
    {
        var known = _instances.FirstOrDefault(x => x.Pid == pid);
        if (known is null || known.Handle == nint.Zero || !IsWindow(known.Handle)) return null;
        if (GetWindowThreadProcessId(known.Handle, out uint owner) == 0 || owner != (uint)pid)
            return null;
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Refresh();
            if (process.HasExited || process.StartTime != known.Started ||
                process.MainWindowHandle != known.Handle) return null;
            return known;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private void ActOnInstance(int pid, string action)
    {
        var instance = ResolveWindow(pid);
        if (instance is null)
        {
            RefreshWindows();
            MessageBox.Show(this, "That Roblox window has closed or changed. The list has been refreshed.",
                "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (action == "focus")
        {
            ShowWindow(instance.Handle, RestoreWindow);
            if (!SetForegroundWindow(instance.Handle))
                MessageBox.Show(this, "Windows prevented switching focus. Select Roblox from the taskbar.",
                    "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else if (action == "minimize")
        {
            ShowWindow(instance.Handle, MinimizeWindow);
        }
        else if (action == "close")
        {
            if (MessageBox.Show(this,
                "Request a normal close for " + instance.Label + "? Unsaved game progress may be lost.\n\n" +
                "Viora will send a window-close request, not force-terminate the game.",
                "Close Roblox window", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            if (!PostMessage(instance.Handle, CloseMessage, nint.Zero, nint.Zero))
                MessageBox.Show(this, "Windows rejected the close request. The game was not force-terminated.",
                    "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void InstanceFocus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int pid }) ActOnInstance(pid, "focus");
    }
    private void InstanceMinimize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int pid }) ActOnInstance(pid, "minimize");
    }
    private void InstanceClose_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int pid }) ActOnInstance(pid, "close");
    }

    private sealed class StoredBox
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private sealed class StoredWorkspace
    {
        public string Name { get; set; } = "";
        public List<StoredBox> Windows { get; set; } = new();
    }

    private sealed class StoredSession
    {
        public string Game { get; set; } = "";
        public int Pid { get; set; }
        public DateTime ProcessStartedUtc { get; set; }
        public DateTime FirstObservedUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public long PeakMemoryBytes { get; set; }
        public double PeakCpuPercent { get; set; }
    }

    private sealed class ManagerData
    {
        public List<StoredWorkspace> Workspaces { get; set; } = new();
        public List<StoredSession> History { get; set; } = new();
    }

    private ManagerData _managerData = new();
    private bool _stateReadOnly;
    private readonly Dictionary<int, StoredSession> _observed = new();
    private Window? _miniWindow;
    private static string DataFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Viora", "Manager", "state.json");

    private void LoadWorkspaces()
    {
        try
        {
            if (File.Exists(DataFile))
                _managerData = JsonSerializer.Deserialize<ManagerData>(File.ReadAllText(DataFile)) ?? new();
            _managerData.Workspaces ??= new();
            _managerData.History ??= new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _managerData = new();
            _stateReadOnly = true;
            MessageBox.Show(this, "Manager state could not be read. It will be kept unchanged; rename state.json manually to re-enable saving.",
                "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        WorkspaceChoice.ItemsSource = _managerData.Workspaces;
        WorkspaceChoice.SelectedIndex = _managerData.Workspaces.Count > 0 ? 0 : -1;
    }

    private void SaveManagerData()
    {
        if (_stateReadOnly) return; // Never overwrite a file we could not parse.
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DataFile)!);
            string temporary = DataFile + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_managerData,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, DataFile, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Could not save Manager data: " + ex.Message,
                "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Session history is locally observed, not a claim about the full
    // lifetime of Roblox or evidence of a crash when a process disappears.
    private void TrackObservedSessions()
    {
        var now = DateTime.UtcNow;
        var current = _instances.ToDictionary(x => x.Pid);
        bool dirty = false;
        foreach (var old in _observed.Keys.ToArray())
        {
            if (current.TryGetValue(old, out var window) &&
                _observed[old].ProcessStartedUtc == window.Started.ToUniversalTime()) continue;
            _managerData.History.Insert(0, _observed[old]);
            _observed.Remove(old);
            dirty = true;
        }
        if (_managerData.History.Count > 100)
        {
            _managerData.History.RemoveRange(100, _managerData.History.Count - 100);
            dirty = true;
        }
        foreach (var window in _instances)
        {
            if (!_observed.TryGetValue(window.Pid, out var item))
            {
                item = new StoredSession
                {
                    Pid = window.Pid,
                    ProcessStartedUtc = window.Started.ToUniversalTime(),
                    FirstObservedUtc = now,
                    Game = window.GameName
                };
                _observed[window.Pid] = item;
            }
            if (!window.GameName.Contains("detect", StringComparison.OrdinalIgnoreCase) &&
                !window.GameName.Contains("Finding game", StringComparison.OrdinalIgnoreCase))
                item.Game = window.GameName;
            item.LastSeenUtc = now;
            item.PeakMemoryBytes = Math.Max(item.PeakMemoryBytes, window.Memory);
            item.PeakCpuPercent = Math.Max(item.PeakCpuPercent, window.CpuPercent);
        }
        if (dirty) SaveManagerData();
    }

    private Rect ScreenAreaPixels()
    {
        var area = SystemParameters.WorkArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Rect(area.X * dpi.DpiScaleX, area.Y * dpi.DpiScaleY,
            area.Width * dpi.DpiScaleX, area.Height * dpi.DpiScaleY);
    }

    private List<Rect> GetPresetLayout(int count, Rect area)
    {
        var rectangles = new List<Rect>();
        int type = LayoutChoice.SelectedIndex;
        if (type == 2 && count > 1)
        {
            double mainWidth = area.Width * 0.66;
            rectangles.Add(new Rect(area.X, area.Y, mainWidth, area.Height));
            for (int i = 1; i < count; i++)
                rectangles.Add(new Rect(area.X + mainWidth,
                    area.Y + (i - 1) * area.Height / (count - 1),
                    area.Width - mainWidth, area.Height / (count - 1)));
        }
        else
        {
            int columns = type == 1 ? count : (int)Math.Ceiling(Math.Sqrt(count));
            int rows = (int)Math.Ceiling(count / (double)columns);
            for (int i = 0; i < count; i++)
                rectangles.Add(new Rect(area.X + i % columns * area.Width / columns,
                    area.Y + i / columns * area.Height / rows,
                    area.Width / columns, area.Height / rows));
        }
        return rectangles;
    }

    private void PositionWindows(IReadOnlyList<Rect> positions)
    {
        int failed = 0;
        for (int i = 0; i < Math.Min(_instances.Count, positions.Count); i++)
        {
            var current = ResolveWindow(_instances[i].Pid);
            if (current is null) { failed++; continue; }
            var box = positions[i];
            ShowWindow(current.Handle, RestoreWindow);
            if (!SetWindowPos(current.Handle, nint.Zero,
                (int)Math.Round(box.Left), (int)Math.Round(box.Top),
                Math.Max(100, (int)Math.Round(box.Width)), Math.Max(100, (int)Math.Round(box.Height)),
                NoZOrder | NoActivate)) failed++;
        }
        if (failed > 0)
            MessageBox.Show(this, failed + " window(s) could not be arranged. Check Windows permissions or refresh.",
                "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ApplySelectedLayout()
    {
        RefreshWindows();
        if (_instances.Count == 0)
        {
            MessageBox.Show(this, "Open a Roblox game first.", "Viora Manager");
            return;
        }
        PositionWindows(GetPresetLayout(_instances.Count, ScreenAreaPixels()));
    }

    private void ApplyLayout_Click(object sender, RoutedEventArgs e) => ApplySelectedLayout();

    private string? AskWorkspaceName()
    {
        var prompt = new Window
        {
            Title = "Save game workspace", Width = 360, Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
            ResizeMode = ResizeMode.NoResize, Background = Brushes.Black
        };
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Workspace name (saved locally)", Margin = new Thickness(0, 0, 0, 9) });
        var input = new TextBox { Text = "My workspace", MaxLength = 40, Height = 29 };
        stack.Children.Add(input);
        var save = new Button { Content = "Save", HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0), Width = 80 };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) prompt.DialogResult = true; };
        stack.Children.Add(save);
        prompt.Content = stack;
        input.SelectAll();
        input.Focus();
        return prompt.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private void SaveWorkspace_Click(object sender, RoutedEventArgs e)
    {
        RefreshWindows();
        if (_stateReadOnly) { MessageBox.Show(this, "Manager state is read-only because state.json could not be loaded. Your existing data is untouched.", "Viora Manager"); return; }
        if (_instances.Count == 0) { MessageBox.Show(this, "No Roblox windows to save.", "Viora Manager"); return; }
        var area = ScreenAreaPixels();
        var positions = new List<StoredBox>();
        foreach (var window in _instances)
        {
            if (ResolveWindow(window.Pid) is null || !GetWindowRect(window.Handle, out var native) ||
                native.Right <= native.Left || native.Bottom <= native.Top)
            {
                MessageBox.Show(this, "Restore all Roblox windows before saving this workspace.",
                    "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            positions.Add(new StoredBox
            {
                X = (native.Left - area.Left) / area.Width,
                Y = (native.Top - area.Top) / area.Height,
                Width = (native.Right - native.Left) / area.Width,
                Height = (native.Bottom - native.Top) / area.Height
            });
        }
        string? name = AskWorkspaceName();
        if (name is null) return;
        var existing = _managerData.Workspaces.FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (MessageBox.Show(this, "Replace existing workspace '" + name + "'?",
                "Viora Manager", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _managerData.Workspaces.Remove(existing);
        }
        var workspace = new StoredWorkspace { Name = name, Windows = positions };
        _managerData.Workspaces.Add(workspace);
        WorkspaceChoice.ItemsSource = null;
        WorkspaceChoice.ItemsSource = _managerData.Workspaces;
        WorkspaceChoice.SelectedItem = workspace;
        SaveManagerData();
    }

    private void RestoreWorkspace_Click(object sender, RoutedEventArgs e)
    {
        RefreshWindows();
        if (WorkspaceChoice.SelectedItem is not StoredWorkspace workspace) return;
        if (workspace.Windows.Count != _instances.Count)
        {
            MessageBox.Show(this,
                "This workspace was saved with " + workspace.Windows.Count + " windows. " +
                "Open exactly that many Roblox windows before restoring it.",
                "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var area = ScreenAreaPixels();
        PositionWindows(workspace.Windows.Select(p => new Rect(
            area.Left + p.X * area.Width, area.Top + p.Y * area.Height,
            Math.Clamp(p.Width, 0.05, 1.0) * area.Width,
            Math.Clamp(p.Height, 0.05, 1.0) * area.Height)).ToList());
    }

    private void ShowHistory_Click(object sender, RoutedEventArgs e)
    {
        var text = new StringBuilder();
        text.AppendLine("SESSION HISTORY · observed while Manager was open");
        text.AppendLine("Previously seen windows are recorded; disappearance does not prove a crash or exit.");
        text.AppendLine();
        if (_managerData.History.Count == 0) text.AppendLine("No completed sessions observed yet.");
        foreach (var row in _managerData.History.Take(75))
        {
            var seconds = Math.Max(0, (row.LastSeenUtc - row.FirstObservedUtc).TotalSeconds);
            text.AppendLine(row.Game + " · PID " + row.Pid);
            text.AppendLine("Seen: " + row.FirstObservedUtc.ToLocalTime().ToString("g") +
                " · Duration observed: " + TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss"));
            text.AppendLine("Peak: " + (row.PeakMemoryBytes / 1048576d).ToString("0") + " MB · CPU " +
                row.PeakCpuPercent.ToString("0.0") + "%");
            text.AppendLine();
        }
        var viewer = new Window
        {
            Title = "Viora Manager — Session history", Width = 560, Height = 520, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        viewer.Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = text.ToString(), TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20), FontSize = 13
            }
        };
        viewer.ShowDialog();
    }

    private void ShowMiniManager_Click(object sender, RoutedEventArgs e)
    {
        if (_miniWindow is not null)
        {
            _miniWindow.Show();
            _miniWindow.Activate();
            return;
        }
        var mini = new Window
        {
            Title = "Viora Mini Manager", Width = 360, Height = 310, MinWidth = 300, MinHeight = 240,
            Topmost = true, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = Math.Max(0, SystemParameters.WorkArea.Right - 390),
            Top = Math.Max(0, SystemParameters.WorkArea.Top + 35),
            Background = new SolidColorBrush(Color.FromRgb(26, 18, 40))
        };
        var panel = new DockPanel { Margin = new Thickness(12) };
        var heading = new TextBlock { Text = "VIORA · LIVE INSTANCES", FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(196, 165, 255)),
            Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(heading, Dock.Top);
        panel.Children.Add(heading);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom);
        var list = new ListBox
        {
            ItemsSource = _cards, DisplayMemberPath = "Summary",
            Background = new SolidColorBrush(Color.FromRgb(35, 25, 54)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            FontSize = 11
        };
        foreach (var action in new[] { "Focus", "Minimise", "Close" })
        {
            var button = new Button { Content = action, Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(0, 0, 7, 0) };
            button.Click += (_, _) =>
            {
                if (list.SelectedItem is InstanceCard card)
                    ActOnInstance(card.Pid, action == "Minimise" ? "minimize" : action.ToLowerInvariant());
            };
            actions.Children.Add(button);
        }
        panel.Children.Add(actions);
        panel.Children.Add(list);
        mini.Content = panel;
        mini.Closed += (_, _) => _miniWindow = null;
        _miniWindow = mini;
        mini.Show();
    }
}
