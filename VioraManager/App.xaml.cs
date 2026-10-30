using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace VioraManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool smokeTest = e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
        try
        {
            var manager = new MainWindow();
            if (smokeTest)
            {
                // Construct the packaged main window and show the live viewer on
                // the runner's WPF desktop, including loading ComboBox templates.
                // This test does not launch, attach to or modify Roblox.
                var viewer = new LiveInstanceViewer(_ => "Game not detected");
                viewer.Show();
                viewer.Close();
                Shutdown(0);
                return;
            }
            manager.Show();
        }
        catch (Exception ex)
        {
            if (smokeTest)
            {
                var report = Environment.GetEnvironmentVariable("VIORA_SMOKE_REPORT");
                if (!string.IsNullOrWhiteSpace(report))
                    File.WriteAllText(report, ex.ToString());
            }
            else
            {
                MessageBox.Show(
                    "Viora Manager could not open.\n\n" + ex.Message +
                    "\n\nPlease report the message in the Viora development thread.",
                    "Viora Manager startup error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown(1);
        }
    }
}
