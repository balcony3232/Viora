using System.Windows;
using System.Windows.Controls;
using System.IO;

namespace Viora;

public partial class MainWindow
{
    private static void SelectSetting(ComboBox combo, int? value)
    {
        if (value is null) return;
        var item = combo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag?.ToString() == value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (item is not null) combo.SelectedItem = item;
    }

    private void RefreshRobloxGraphics()
    {
        if (PerfSettingsStatus is null) return;
        try
        {
            var settings = RobloxGraphicsSettings.Read();
            if (!settings.HasFile)
            {
                PerfSettingsStatus.Text = "Roblox hasn't created its settings file yet. Open Roblox once, close it, then reload.";
                return;
            }
            SelectSetting(PerfFpsCombo, settings.Fps);
            SelectSetting(PerfQualityCombo, settings.Quality);
            PerfSettingsStatus.Text = "Saved on this PC: " +
                (settings.Fps is null ? "FPS cap not exposed" : settings.Fps + " FPS") + " · " +
                (settings.Quality is null ? "Graphics quality not exposed" : (settings.Quality == 0 ? "Automatic quality" : "Quality level " + settings.Quality)) +
                (RobloxGraphicsSettings.HasBackup ? " · Viora restore point available" : "");
        }
        catch (Exception ex) { PerfSettingsStatus.Text = "Could not read Roblox settings: " + ex.Message; }
    }

    private void ReloadRobloxGraphics_Click(object sender, RoutedEventArgs e) => RefreshRobloxGraphics();

    private void ApplyRobloxGraphics_Click(object sender, RoutedEventArgs e)
    {
        if (PerfFpsCombo.SelectedItem is not ComboBoxItem fpsItem ||
            PerfQualityCombo.SelectedItem is not ComboBoxItem qualityItem ||
            !int.TryParse(fpsItem.Tag?.ToString(), out var fps) ||
            !int.TryParse(qualityItem.Tag?.ToString(), out var quality)) return;
        var label = quality == 0 ? "Automatic" : "Level " + quality;
        if (MessageBox.Show($"Write {fps} FPS and graphics {label} to saved Roblox settings?\n\nClose Roblox first. A restore point will be saved on the first edit.",
            "Apply Roblox graphics", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            RobloxGraphicsSettings.Apply(fps, quality);
            RefreshRobloxGraphics();
            SetStatus($"Saved {fps} FPS and graphics {label}. Restart Roblox to test.");
        }
        catch (Exception ex) { Alert("Could not save Roblox graphics: " + ex.Message); RefreshRobloxGraphics(); }
    }

    private void RestoreRobloxGraphics_Click(object sender, RoutedEventArgs e)
    {
        if (!RobloxGraphicsSettings.HasBackup)
        {
            Alert("There is no previous Viora graphics configuration to restore.");
            return;
        }
        if (MessageBox.Show("Restore only the FPS cap and saved graphics quality values from before Viora edited them? Other Roblox settings will stay as they are.",
            "Restore graphics", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            if (RobloxGraphicsSettings.Restore()) SetStatus("Restored the previous FPS and graphics quality settings.");
            RefreshRobloxGraphics();
        }
        catch (Exception ex) { Alert("Could not restore Roblox graphics: " + ex.Message); RefreshRobloxGraphics(); }
    }
}
