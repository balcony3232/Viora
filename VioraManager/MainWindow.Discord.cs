using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VioraManager;

public partial class MainWindow
{
    private void DiscordRpcSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Viora Manager — Discord Rich Presence",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Width = 510,
            Height = 455,
            Background = new SolidColorBrush(Color.FromRgb(16, 11, 26)),
            Foreground = Brushes.White,
            ShowInTaskbar = false
        };
        var root = new StackPanel { Margin = new Thickness(22) };
        dialog.Content = root;
        root.Children.Add(new TextBlock
        {
            Text = "DISCORD RICH PRESENCE",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(205, 169, 255)),
            Margin = new Thickness(0, 0, 0, 10)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Automatically display the number of Roblox instances and their combined CPU / approximate 3D GPU usage while Viora Manager is running.",
            Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 15)
        });
        var enabled = new CheckBox
        {
            Content = "Enable automatically when Viora Manager starts",
            IsChecked = _discordPresence.Enabled, Margin = new Thickness(0, 0, 0, 14),
            Foreground = Brushes.White
        };
        root.Children.Add(enabled);
        root.Children.Add(new TextBlock
        {
            Text = "Discord Application ID (15–25 digits)",
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 0, 0, 5)
        });
        var appId = new TextBox
        {
            Text = _discordPresence.ApplicationId,
            MaxLength = 25, Height = 34,
            ToolTip = "Your Discord Developer Portal Application ID. Never paste a bot token or secret."
        };
        root.Children.Add(appId);
        root.Children.Add(new TextBlock
        {
            Text = _discordPresence.UsingSharedAppId
                ? "Detected the Application ID already saved in Viora's main app."
                : "Use an Application ID from Discord's Developer Portal, or configure one in the main Viora app.",
            Foreground = new SolidColorBrush(Color.FromRgb(186, 161, 211)),
            FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 14)
        });
        var includeGpu = new CheckBox
        {
            Content = "Include GPU 3D engine estimate (when available)",
            IsChecked = _discordPresence.IncludeGpuEstimate,
            Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 14)
        };
        root.Children.Add(includeGpu);
        root.Children.Add(new TextBlock
        {
            Text = "Discord desktop must be running. The GPU value is an estimate from Windows performance counters, not a hardware-sensor reading. If Viora's main app also publishes RPC, the two activities may replace one another.",
            Foreground = Brushes.LightGray, FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var portal = new Button { Content = "Discord Developer Portal", Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 8, 0) };
        portal.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://discord.com/developers/applications") { UseShellExecute = true }); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(dialog, ex.Message, "Could not open browser", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        buttons.Children.Add(portal);
        var save = new Button { Content = "Save", Padding = new Thickness(20, 7, 20, 7) };
        save.Click += (_, _) =>
        {
            try
            {
                _discordPresence.SaveSettings(enabled.IsChecked == true,
                    appId.Text.Trim(), includeGpu.IsChecked == true);
                RpcStatusText.Text = _discordPresence.Enabled ? "RPC configured" : "RPC off";
                RpcStatusText.ToolTip = _discordPresence.Status;
                dialog.DialogResult = true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(dialog, ex.Message, "Discord RPC configuration",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        buttons.Children.Add(save);
        root.Children.Add(buttons);
        dialog.ShowDialog();
    }
}
