using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace VioraManager;

/// <summary>
/// Session-only, user-initiated idle helper. When the user has actually left
/// the PC idle, it may briefly focus each existing Roblox window and send one
/// Space tap as ordinary Windows input. Never sends to background windows,
/// never injects into Roblox, and never claims to prevent every timeout.
/// </summary>
internal sealed class RobloxAntiAfk
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const ushort SpaceKey = 0x20;
    private const int Restore = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Time; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Msg;
        public ushort ParamLow;
        public ushort ParamHigh;
    }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public HardwareInput Hardware;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hwnd);

    private readonly Dictionary<int, DateTimeOffset> _lastPulses = new();
    private DateTimeOffset _enabledAt = DateTimeOffset.MinValue;
    private bool _busy;

    public bool Enabled { get; private set; }
    public string Status { get; private set; } = "Off";

    public void Toggle(bool enabled)
    {
        Enabled = enabled;
        _lastPulses.Clear();
        _enabledAt = DateTimeOffset.UtcNow;
        Status = enabled
            ? "Enabled for this session. If you leave Windows idle for 5+ minutes, Viora attempts a Space tap on Roblox every ~8 minutes. Windows may block switching."
            : "Off";
    }

    public async Task TickAsync(IReadOnlyList<(int Pid, nint Hwnd)> windows)
    {
        if (!Enabled || _busy) return;
        if (DateTimeOffset.UtcNow - _enabledAt < TimeSpan.FromMinutes(7)) return;
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) { Status = "Windows idle-time measurement unavailable."; return; }
        // Read idle time ONCE before any simulated input, since SendInput may
        // itself change the OS idle counter. Do nothing during active use.
        uint idleMs = unchecked((uint)Environment.TickCount - info.Time);
        if (idleMs < 5 * 60 * 1000) return;

        var now = DateTimeOffset.UtcNow;
        var active = windows.Where(w => w.Hwnd != nint.Zero && IsWindow(w.Hwnd)).ToList();
        foreach (var pid in _lastPulses.Keys.Except(active.Select(x => x.Pid)).ToArray())
            _lastPulses.Remove(pid);

        _busy = true;
        int sent = 0, blocked = 0;
        try
        {
            foreach (var (pid, hwnd) in active)
            {
                if (!Enabled) break;
                if (_lastPulses.TryGetValue(pid, out var previous) &&
                    now - previous < TimeSpan.FromMinutes(8)) continue;

                // Windows blocks many background focus requests. Never send
                // keys if foreground verification fails.
                _ = ShowWindow(hwnd, Restore);
                _ = SetForegroundWindow(hwnd);
                await Task.Delay(130);
                if (GetForegroundWindow() != hwnd) { blocked++; continue; }

                var events = new[]
                {
                    new Input { Type = InputKeyboard, Data = new InputUnion
                        { Keyboard = new KeyInput { VirtualKey = SpaceKey } } },
                    new Input { Type = InputKeyboard, Data = new InputUnion
                        { Keyboard = new KeyInput { VirtualKey = SpaceKey, Flags = KeyUp } } }
                };
                if (SendInput((uint)events.Length, events, Marshal.SizeOf<Input>()) == 2)
                {
                    _lastPulses[pid] = now;
                    sent++;
                }
                else blocked++;
                await Task.Delay(130);
            }
            if (sent > 0)
                Status = "Space pulse attempted on " + sent + " Roblox window(s) at " +
                    DateTime.Now.ToString("HH:mm") + ". This does not guarantee Roblox counts it as activity.";
            else if (blocked > 0)
                Status = "Windows prevented Roblox window focus/input (" + blocked +
                    " skipped). Anti-AFK may not work on this PC.";
        }
        finally { _busy = false; }
    }
}
