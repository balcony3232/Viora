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

    /// <summary>
    /// Does not silently enable the legacy behavior. Always ask and decline
    /// safely if any Roblox Player process is already active.
    /// </summary>
    private bool EnsureLegacyMultiInstanceMode()
    {
        if (_experiment.IsEnabled)
        {
            RefreshLegacyModeIndicator();
            return true;
        }
        if (GetAllPlayerPids().Count > 0)
        {
            AccountLaunchStatus.Text =
                "Close all Roblox Player clients before enabling experimental multi-instance mode. " +
                "No browser windows were opened by this launch attempt.";
            MessageBox.Show(this,
                "Roblox Player is already running. The legacy compatibility setting must start " +
                "BEFORE the first Player instance.\n\n" +
                "Close every Roblox Player window (not just browser tabs), then try Launch selected again.",
                "Close Roblox Player first", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var choice = MessageBox.Show(this,
            "To try launching several Roblox Players, Viora can enable its existing legacy " +
            "multi-instance experiment before opening the first game.\n\n" +
            "This is NOT officially supported by Roblox, can stop working after updates, " +
            "and may carry account risks. It won't edit Roblox binaries or access Roblox login cookies.\n\n" +
            "The experiment turns off when Manager exits. Enable it for this session?",
            "Enable experimental multi-instance mode?",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            AccountLaunchStatus.Text = "Batch cancelled. Multi-instance mode was not enabled.";
            return false;
        }

        if (!_experiment.TryEnable(out string result))
        {
            ExperimentStatus.Text = result;
            AccountLaunchStatus.Text = result;
            RefreshLegacyModeIndicator();
            MessageBox.Show(this, result, "Could not enable legacy multi-instance compatibility",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        ExperimentStatus.Text = result;
        AccountLaunchStatus.Text = "Legacy experiment active. Waiting for actual Roblox Player windows.";
        RefreshLegacyModeIndicator();
        return true;
    }

    private static HashSet<int> GetAllPlayerPids()
    {
        var ids = new HashSet<int>();
        var processes = Process.GetProcessesByName("RobloxPlayerBeta");
        try
        {
            foreach (var process in processes)
            {
                try { if (!process.HasExited) ids.Add(process.Id); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        return ids;
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
            if (current.Except(before).Any()) return true;
            progress(current.Count);
        }
        return false;
    }
}
