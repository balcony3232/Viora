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
                // This checks that packaged XAML and the main window can be constructed.
                // No Roblox windows are launched or modified during the smoke test.
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
