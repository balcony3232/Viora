using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Viora;

// Separate non-modal help centre. Diagnostic export is an explicit user action.
public sealed class HelpPrivacyWindow : Window
{
    private readonly TextBox _report = new();
    private string _preview = "";

    public HelpPrivacyWindow(int startTab = 0)
    {
        Title = "Viora — Getting Started & Privacy";
        Width = 770; Height = 630; MinWidth = 680; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(14, 11, 24));
        Foreground = Brushes.White;
        var root = new DockPanel { Margin = new Thickness(23) };
        Content = root;
        var heading = Label("Getting Started & Privacy", 26, Brushes.White);
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
        var tabs = new TabControl { Margin = new Thickness(0, 14, 0, 12) };
        root.Children.Add(tabs);

        tabs.Items.Add(Page("Start here",
            ("1. Install", "Download Viora Setup from the official GitHub Releases page, or use the portable EXE. Run only trusted downloads."),
            ("2. Sign in", "Online-enabled builds use Supabase email/password accounts with email verification and globally unique usernames. Existing local accounts remain available in legacy offline mode; Viora never requests your Roblox password."),
            ("3. Roblox", "The startup wizard detects the official Roblox Player. Launch from Overview or save favourite game links in Quick Launcher."),
            ("4. Optimise", "Graphics and FastFlag options are available under Optimise. Roblox may ignore unsupported flags. Save a backup before experimenting."),
            ("5. Discord", "Discord Rich Presence is optional. Enter an Application ID on the Discord page. Never enter a bot token, client secret or Roblox password."),
            ("6. Updates and support", "Utilities contains update checks and backups. Issue reports go through GitHub after reviewing attached content.")));
        tabs.Items.Add(Page("Privacy",
            ("Local data", "Viora keeps FastFlag profiles, settings, game history and custom media on this PC. Existing legacy local accounts may also store a salted password hash."),
            ("External services", "Online-enabled builds send account email and password securely to Supabase Auth for sign-in. Global usernames, public IDs and roles live on the server. Public playtime ranking is opt-in; when enabled, Viora sends a periodic Roblox-running signal. GitHub, public Roblox APIs and optional Discord Rich Presence are separate."),
            ("Sensitive backups", "Full recovery ZIP files can contain password hashes and playtime history. Never upload them publicly."),
            ("Data removal", "Uninstalling Viora leaves local settings for upgrades. Deleting local AppData does not delete a Supabase account or online playtime. Online account deletion must be requested separately until the deletion flow is implemented."),
            ("Account limitations", "Local account sign-in helps with accidental access, but it does not protect data from someone who can modify your Windows user files.")));
        var diagnostics = new StackPanel { Margin = new Thickness(16) };
        diagnostics.Children.Add(Label("Safe diagnostic preview", 19, Brushes.White));
        diagnostics.Children.Add(Paragraph("Only the Viora version, platform and whether a Roblox installation was detected are included. No paths, account data, game history, tokens or logs are attached."));
        _report.Height = 160; _report.IsReadOnly = true; _report.TextWrapping = TextWrapping.Wrap;
        _report.FontFamily = new FontFamily("Consolas"); _report.Margin = new Thickness(0, 12, 0, 12);
        diagnostics.Children.Add(_report);
        var buttons = new WrapPanel();
        var refresh = new Button { Content = "Refresh preview", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(13, 8, 13, 8) };
        refresh.Click += (_, _) => RefreshReport();
        buttons.Children.Add(refresh);
        var export = new Button { Content = "Export reviewed JSON", Padding = new Thickness(13, 8, 13, 8) };
        export.Click += Export_Click; buttons.Children.Add(export);
        diagnostics.Children.Add(buttons);
        diagnostics.Children.Add(Paragraph("The report is saved locally only. Nothing is uploaded automatically."));
        tabs.Items.Add(new TabItem { Header = "Safe diagnostics", Content = new ScrollViewer { Content = diagnostics } });
        tabs.Items.Add(Page("Multi-instance",
            ("Compatibility test", "Test only with official unmodified Roblox: open one Player game, then attempt another normal Roblox launch. Observe whether the second game opens independently, reuses the first, or fails."),
            ("What counts as success?", "Two independent and usable Player windows must remain open. Test closing one and joining a different server."),
            ("Limits", "Roblox has restricted simultaneous Player clients. Viora does not disable client protections, remove single-instance locks, or bypass anti-cheat."),
            ("Official alternative", "Roblox Studio's Server & Clients testing mode supports up to eight simulated clients for developer multiplayer tests.")));
        tabs.SelectedIndex = Math.Clamp(startTab, 0, tabs.Items.Count - 1);
        RefreshReport();
    }

    private static TextBlock Label(string title, int size, Brush color)
        => new() { Text = title, FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = color, Margin = new Thickness(0, 0, 0, 10) };
    private static TextBlock Paragraph(string text)
        => new() { Text = text, Foreground = Brushes.Gainsboro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) };
    private static TabItem Page(string name, params (string Title, string Text)[] parts)
    {
        var panel = new StackPanel { Margin = new Thickness(18) };
        foreach (var part in parts)
        {
            panel.Children.Add(Label(part.Title, 17, new SolidColorBrush(Color.FromRgb(201, 173, 255))));
            panel.Children.Add(Paragraph(part.Text));
        }
        return new TabItem { Header = name, Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel } };
    }
    private void RefreshReport()
    {
        bool? hasRoblox;
        try { hasRoblox = RobloxService.FindInstallations().Count > 0; }
        catch { hasRoblox = null; }
        _preview = JsonSerializer.Serialize(new {
            format = "viora-diagnostic-v1",
            version = typeof(HelpPrivacyWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown",
            platform = "Windows",
            robloxDetected = hasRoblox
        }, new JsonSerializerOptions { WriteIndented = true });
        _report.Text = _preview;
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Save reviewed diagnostic report", FileName = "viora-diagnostics.json", Filter = "JSON files|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, _preview);
            MessageBox.Show(this, "Diagnostic report saved locally; nothing was uploaded.", "Viora", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, "Could not export report: " + ex.Message); }
    }
}