using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Viora;

public partial class MainWindow
{
    private void OpenVioraManager_Click(object sender, RoutedEventArgs e)
    {
        string manager = Path.Combine(AppContext.BaseDirectory, "VioraManager.exe");
        if (!File.Exists(manager))
        {
            MessageBox.Show(this,
                "Viora Manager is an optional application. Run Viora Setup and select the Viora Manager component, or download the separate Manager EXE from an official release.",
                "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(manager) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Unable to open Viora Manager: " + ex.Message,
                "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}