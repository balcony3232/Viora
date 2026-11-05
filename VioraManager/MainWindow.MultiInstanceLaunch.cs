using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace VioraManager;

/// <summary>
/// Best-effort, opt-in legacy compatibility and observable Player launch checks.
/// This does not inject, patch, terminate, manipulate remote process handles,
/// read Roblox credentials, or claim Roblox supports multiple Player clients.
/// </summary>
public partial class MainWindow
{
    private void RefreshLegacyModeIndicator()
    {
        if (AccountMultiInstanceButton is null) return;
        bool enabled = _experiment.IsEnabled;
        AccountMultiInstanceButton.Content = enabled
            ? "✓ Multi-instance: experimental ON" : "Multi-instance: OFF";
        AccountMultiInstanceButton.ToolTip = enabled
            ? "Legacy compatibility is active until Manager exits. Roblox can still restrict additional clients."
            : "Click before launching Roblox to try experimental legacy multi-instance compatibility.";
        MultiInstanceToggle.IsChecked = enabled;
    }

    private void MultiInstanceCheckedChanged(object sender, RoutedEventArgs e)
    {
        // The other tab can change the setting too; queue status must stay in sync.
        // Defer until after its Click handler has completed any confirmation.
        Dispatcher.BeginInvoke(new Action(RefreshLegacyModeIndicator));
    }

    private void AccountMultiInstance_Click(object sender, RoutedEventArgs e)
    {
        if (_accountLaunchCancel is not null)
        {
            AccountLaunchStatus.Text = "Stop the current launch queue before changing multi-instance settings.";
            return;
        }
        MultiInstanceToggle.IsChecked = !_experiment.IsEnabled;
        MultiInstanceToggle_Click(MultiInstanceToggle, e);
        RefreshLegacyModeIndicator();
    }

    private static HashSet<int> GetVisiblePlayerPids()
    {
        var ids = new HashSet<int>();
        var processes = Process.GetProcessesByName("RobloxPlayerBeta");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    process.Refresh();
                    if (!process.HasExited && process.MainWindowHandle != nint.Zero &&
                        IsWindowVisible(process.MainWindowHandle))
                        ids.Add(process.Id);
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        return ids;
    }

    private static async Task<bool> WaitForAdditionalPlayerAsync(
        HashSet<int> before, int timeoutSeconds, CancellationToken cancel,
        Action<int> progress)
    {
        for (int elapsed = 0; elapsed < timeoutSeconds; elapsed++)
        {
            cancel.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(1), cancel);
            var current = GetVisiblePlayerPids();
            // A changed PID by itself is not another Player if the first one
            // was replaced or exited. Require a net additional *visible* client.
            if (current.Count > before.Count && current.Except(before).Any()) return true;
            progress(current.Count);
        }
        return false;
    }
}
