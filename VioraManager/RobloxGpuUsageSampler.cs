using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace VioraManager;

/// <summary>
/// Optional Windows GPU Engine performance counter sampler (process-scoped 3D
/// engines only). This is a best-effort estimate, not GPU hardware telemetry.
/// Missing/inaccessible counters return null: never fabricate a percentage.
/// </summary>
internal sealed class RobloxGpuUsageSampler : IDisposable
{
    private const uint FormatDouble = 0x00000200;
    private const int MoreData = unchecked((int)0x800007D2);
    private nint _query;
    private nint _counter;
    private bool _primed;

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhValueItem
    {
        public nint Name;
        public uint Status;
        // Native PDH_FMT_COUNTERVALUE has 4 bytes padding on x64.
        public double Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQuery(string? dataSource, nint userData, out nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")]
    private static extern int PdhAddEnglishCounter(nint query, string path, nint userData, out nint counter);
    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static extern int PdhGetFormattedCounterArray(nint counter, uint format, ref uint bytes,
        ref uint count, nint valueArray);
    [DllImport("pdh.dll")]
    private static extern int PdhCloseQuery(nint query);

    public RobloxGpuUsageSampler()
    {
        try
        {
            if (PdhOpenQuery(null, nint.Zero, out _query) != 0 || _query == nint.Zero)
                return;
            if (PdhAddEnglishCounter(_query,
                @"\GPU Engine(*)\Utilization Percentage", nint.Zero, out _counter) != 0)
                _counter = nint.Zero;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _counter = nint.Zero;
        }
    }

    public double? Sample(IReadOnlyCollection<int> robloxPids)
    {
        if (_counter == nint.Zero) return null;
        try
        {
            if (PdhCollectQueryData(_query) != 0) return null;
            if (!_primed) { _primed = true; return null; }
            uint bytes = 0, count = 0;
            int status = PdhGetFormattedCounterArray(_counter, FormatDouble,
                ref bytes, ref count, nint.Zero);
            if (status != MoreData || bytes == 0 || bytes > 2_000_000) return null;
            nint buffer = Marshal.AllocHGlobal(checked((int)bytes));
            try
            {
                if (PdhGetFormattedCounterArray(_counter, FormatDouble,
                    ref bytes, ref count, buffer) != 0)
                    return null;

                var allowed = new HashSet<int>(robloxPids);
                double total = 0;
                int stride = Marshal.SizeOf<PdhValueItem>();
                for (int i = 0; i < count; i++)
                {
                    var entry = Marshal.PtrToStructure<PdhValueItem>(buffer + i * stride);
                    if (entry.Status != 0 || !double.IsFinite(entry.Value)) continue;
                    string name = Marshal.PtrToStringUni(entry.Name) ?? "";
                    // Windows instance names include pid_<decimal> and
                    // engtype_3D, for example pid_1234_luid_..._engtype_3D.
                    if (!name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                        continue;
                    int pidMarker = name.IndexOf("pid_", StringComparison.OrdinalIgnoreCase);
                    if (pidMarker < 0) continue;
                    int start = pidMarker + 4;
                    int end = start;
                    while (end < name.Length && char.IsAsciiDigit(name[end])) end++;
                    if (!int.TryParse(name[start..end], NumberStyles.None,
                        CultureInfo.InvariantCulture, out var pid) || !allowed.Contains(pid))
                        continue;
                    total += Math.Max(0, entry.Value);
                }
                return Math.Clamp(total, 0, 100);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException
                                   or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_query == nint.Zero) return;
        _ = PdhCloseQuery(_query);
        _query = nint.Zero;
        _counter = nint.Zero;
    }
}
