using System.Windows;

namespace Viora;

public partial class MainWindow
{
    private void GettingStarted_Click(object sender, RoutedEventArgs e)
        => new HelpPrivacyWindow(0) { Owner = this }.ShowDialog();

    private void PrivacyGuide_Click(object sender, RoutedEventArgs e)
        => new HelpPrivacyWindow(1) { Owner = this }.ShowDialog();
}
