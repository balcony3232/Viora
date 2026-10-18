using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Viora;

public partial class MainWindow
{
    private void InitializePolish()
    {
        FooterVersionText.Text = "VIORA  ·  v" +
            (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown");

        // Run before the nested chart/tiles consume the mouse wheel.
        // Leave combo boxes, sliders and scrollbars to handle their own wheel events.
        foreach (var page in new[] {
            DashboardPage, StatsPage, AppearancePage, GameHubPage, DiscordPage,
            PerformancePage, ModsPage, SplashPage,
            LauncherPage, ToolsPage })
        {
            page.PreviewMouseWheel += PagePreviewMouseWheel;
            page.PanningMode = PanningMode.VerticalOnly;
        }
    }

    private static bool IsInteractiveWheelTarget(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ComboBox or Slider or ScrollBar or DataGrid) return true;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    private static void PagePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer page || page.ScrollableHeight <= 0) return;
        if (IsInteractiveWheelTarget(e.OriginalSource as DependencyObject)) return;
        // Normalised wheel movement, including high-resolution wheels and touchpads.
        double next = Math.Clamp(page.VerticalOffset - (e.Delta / 120d) * 100d,
            0, page.ScrollableHeight);
        if (Math.Abs(next - page.VerticalOffset) < 0.1) return;
        page.ScrollToVerticalOffset(next);
        e.Handled = true;
    }

    private void ReportIssue_Click(object sender, RoutedEventArgs e)
        => new IssueReportWindow { Owner = this }.ShowDialog();
}
