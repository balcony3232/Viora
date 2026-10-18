using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Viora;

public partial class App : Application
{
    public App()
    {
        // Capture fatal managed exceptions that a WPF button's try/catch
        // cannot handle (e.g. deferred Dispatcher bindings or async work).
        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                CrashDiagnostics.Record("AppDomain", exception);
            else
                CrashDiagnostics.Mark("Fatal AppDomain exception (no managed Exception object)");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashDiagnostics.Record("UnobservedTask", args.Exception);
            args.SetObserved();
        };
    }

    private static void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashDiagnostics.Record("WPF Dispatcher", e.Exception);
        try
        {
            MessageBox.Show(
                "Viora encountered an unexpected error. A private diagnostic log was saved at:\n\n" +
                CrashDiagnostics.LogPath + "\n\nReview the file for personal paths before sharing it.",
                "Viora — Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { /* Avoid secondary errors while diagnosing a crash. */ }

        // Don't mark the exception handled: the app may be in an unsafe state.
        // A native crash or StackOverflowException still requires Event Viewer.
    }
}
