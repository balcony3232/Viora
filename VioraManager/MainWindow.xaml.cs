using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Threading;

namespace VioraManager;

public partial class MainWindow : Window
{
    private sealed record RunningWindow(int Pid, nint Handle, DateTime Started, long Memory, double CpuPercent, bool IsResponsive)
    {
        public string Label { get; init; } = "";
        public string GameName { get; init; } = "Game not detected";
        public string Detail => $"PID {Pid} · {Math.Max(0, (int)(DateTime.Now - Started).TotalMinutes)}m runtime · {Memory / 1048576d:0} MB RAM · CPU {CpuPercent:0.0}%";
    }

    // Stable binding objects: avoid tearing down/recreating every WPF instance
    // card on each poll, which can cause flicker and unnecessary layout work.
    private sealed class InstanceCard : INotifyPropertyChanged
    {
        public int Pid { get; }
        private string _label = "";
        private string _gameName = "";
        private string _detail = "";
        private string _status = "Running";
        public string Label => _label;
        public string GameName => _gameName;
        public string Detail => _detail;
        public string Status => _status;
        public string Summary => $"{_label} · {_gameName} · {_detail}";
        public event PropertyChangedEventHandler? PropertyChanged;

        public InstanceCard(int pid) => Pid = pid;

        public void Update(string label, string gameName, string detail, string status)
        {
            bool changed = false;
            if (_label != label) { _label = label; changed = true; PropertyChanged?.Invoke(this, new(nameof(Label))); }
            if (_gameName != gameName) { _gameName = gameName; changed = true; PropertyChanged?.Invoke(this, new(nameof(GameName))); }
            if (_detail != detail) { _detail = detail; changed = true; PropertyChanged?.Invoke(this, new(nameof(Detail))); }
            if (_status != status) { _status = status; changed = true; PropertyChanged?.Invoke(this, new(nameof(Status))); }
            if (changed) PropertyChanged?.Invoke(this, new(nameof(Summary)));
        }
    }

    private readonly ObservableCollection<InstanceCard> _cards = new();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly MultiInstanceExperiment _experiment = new();
    private readonly RobloxInstanceGames _games;
    private bool _loadingAutoPreference;
    private static readonly string AutoPreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Viora", "Manager", "startup-preferences.json");

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
        ManagerVersionText.Text = "v" + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown") + " Preview";
        InstanceList.ItemsSource = _cards;
        _games = new RobloxInstanceGames(() => Dispatcher.BeginInvoke(new Action(RefreshWindows)));
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        _refresh.Tick += (_, _) => RefreshWindows();
        Loaded += (_, _) => { LoadAutoMultiInstancePreference(); LoadWorkspaces(); RefreshWindows(); _refresh.Start(); };
        Closed += (_, _) => { _refresh.Stop(); _experiment.Disable(); _liveViewer?.Close(); _miniWindow?.Close(); };
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
                        process.StartTime, process.WorkingSet64,
                        SampleCpu(process.Id, process.StartTime, process.TotalProcessorTime),
                        !IsHungAppWindow(process.MainWindowHandle)));
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
        // Only create/remove/move visual cards when the actual processes change.
        // Preserve binding objects for unchanged PIDs and notify on changed fields.
        var activeIds = _instances.Select(x => x.Pid).ToHashSet();
        foreach (var deadPid in _cpuSamples.Keys.Where(pid => !activeIds.Contains(pid)).ToArray())
            _cpuSamples.Remove(deadPid);
        TrackObservedSessions();
        for (int i = _cards.Count - 1; i >= 0; i--)
            if (!activeIds.Contains(_cards[i].Pid)) _cards.RemoveAt(i);

        for (int i = 0; i < _instances.Count; i++)
        {
            var window = _instances[i];
            var card = _cards.FirstOrDefault(x => x.Pid == window.Pid);
            if (card is null)
            {
                card = new InstanceCard(window.Pid);
                _cards.Insert(i, card);
            }
            else
            {
                int oldIndex = _cards.IndexOf(card);
                if (oldIndex != i) _cards.Move(oldIndex, i);
            }
            card.Update(window.Label, window.GameName, window.Detail,
                window.IsResponsive ? "Running" : "Not responding");
        }

        string instanceCount = _instances.Count.ToString();
        if (InstanceCount.Text != instanceCount) InstanceCount.Text = instanceCount;
        long total = _instances.Sum(x => x.Memory);
        string memory = total >= 1073741824 ? $"{total / 1073741824d:0.0} GB" : $"{total / 1048576d:0} MB";
        if (MemoryText.Text != memory) MemoryText.Text = memory;
        string cpu = $"{_instances.Sum(x => x.CpuPercent):0.0}%";
        if (CpuText.Text != cpu) CpuText.Text = cpu;
        string status = _instances.Count > 0 ? "Active" : "Idle";
        if (StatusText.Text != status) StatusText.Text = status;
        EmptyInstancesText.Visibility = _instances.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Poll less often when Roblox isn't running; return to the normal
        // three-second cadence once a visible session is detected.
        var wantedInterval = TimeSpan.FromSeconds(_instances.Count == 0 ? 5 : 3);
        if (_refresh.Interval != wantedInterval) _refresh.Interval = wantedInterval;
    }

    private static bool HasRobloxPlayerRunning()
    {
        var processes = Process.GetProcessesByName("RobloxPlayerBeta");
        try
        {
            return processes.Any(p =>
            {
                try { return !p.HasExited; }
                catch (InvalidOperationException) { return false; }
            });
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    // This opt-in is enabled by default only in the feature preview. Users may
    // disable it at any time and the choice survives subsequent launches.
    private void LoadAutoMultiInstancePreference()
    {
        bool autoEnable = true;
        try
        {
            if (File.Exists(AutoPreferencePath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(AutoPreferencePath));
                if (document.RootElement.TryGetProperty("autoEnableMultiInstance", out var value)
                    && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    autoEnable = value.GetBoolean();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ExperimentStatus.Text = "Could not read saved startup preference; using preview default.";
        }

        _loadingAutoPreference = true;
        AutoMultiInstanceToggle.IsChecked = autoEnable;
        _loadingAutoPreference = false;
        if (autoEnable) TryAutoEnableMultiInstance();
    }

    private void SaveAutoMultiInstancePreference(bool enabled)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AutoPreferencePath)!);
            File.WriteAllText(AutoPreferencePath,
                JsonSerializer.Serialize(new { autoEnableMultiInstance = enabled }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ExperimentStatus.Text = "Could not save multi-instance startup preference: " + ex.Message;
        }
    }

    private void TryAutoEnableMultiInstance()
    {
        if (_experiment.IsEnabled)
        {
            MultiInstanceToggle.IsChecked = true;
            return;
        }
        if (HasRobloxPlayerRunning())
        {
            MultiInstanceToggle.IsChecked = false;
            ExperimentStatus.Text =
                "Auto-start skipped: Roblox is already running. Exit Roblox completely and restart Viora Manager to enable compatibility.";
            return;
        }
        bool enabled = _experiment.TryEnable(out string message);
        MultiInstanceToggle.IsChecked = enabled;
        ExperimentStatus.Text = enabled
            ? "Automatically enabled for this session. " + message
            : "Auto-start unavailable: " + message;
    }

    private void AutoMultiInstanceToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingAutoPreference) return;
        bool enabled = AutoMultiInstanceToggle.IsChecked == true;
        SaveAutoMultiInstancePreference(enabled);
        if (enabled) TryAutoEnableMultiInstance();
        else
        {
            _experiment.Disable();
            MultiInstanceToggle.IsChecked = false;
            ExperimentStatus.Text = "Automatic multi-instance startup disabled. Compatibility mode is off.";
        }
    }

    private void MultiInstanceToggle_Click(object sender, RoutedEventArgs e)
    {
        if (MultiInstanceToggle.IsChecked != true)
        {
            _experiment.Disable();
            AutoMultiInstanceToggle.IsChecked = false;
            SaveAutoMultiInstancePreference(false);
            ExperimentStatus.Text = "Experimental mode disabled; automatic startup also disabled.";
            return;
        }

        if (HasRobloxPlayerRunning())
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
            AutoMultiInstanceToggle.IsChecked = true;
            SaveAutoMultiInstancePreference(true);
            ExperimentStatus.Text = message;
        }
        else
        {
            MultiInstanceToggle.IsChecked = false;
            ExperimentStatus.Text = message;
            MessageBox.Show(this, message, "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private LiveInstanceViewer? _liveViewer;

    private void ShowLiveViewer_Click(object sender, RoutedEventArgs e)
    {
        if (_liveViewer is { IsLoaded: true }) { _liveViewer.Activate(); return; }
        _liveViewer = new LiveInstanceViewer(pid => _games.GetGameName(pid)) { Owner = this };
        _liveViewer.Show();
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
        ApplySelectedLayout();
    }
}
