using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Viora;

/// <summary>Explicit, user-reviewed text hand-off. Never sends reports automatically.</summary>
public sealed class IssueReportWindow : Window
{
    private readonly TextBox _title = new();
    private readonly TextBox _steps = new();
    private readonly TextBox _expected = new();
    private readonly TextBox _actual = new();
    private readonly TextBox _preview = new();
    private readonly CheckBox _diagnostics = new() { IsChecked = true };

    public IssueReportWindow()
    {
        Title = "Viora — Report an issue";
        Width = 725; Height = 760; MinWidth = 630; MinHeight = 590;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(16, 12, 26));
        Foreground = Brushes.White;
        var layout = new DockPanel { Margin = new Thickness(26, 21, 26, 21) };
        Content = layout;

        var footer = new StackPanel { Orientation = Orientation.Vertical };
        DockPanel.SetDock(footer, Dock.Bottom);
        var buttons = new WrapPanel { Margin = new Thickness(0, 11, 0, 0) };
        var send = new Button { Content = "Copy report & open GitHub Issues", Padding = new Thickness(17, 10, 17, 10) };
        send.Click += Submit_Click;
        buttons.Children.Add(send);
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(15, 10, 15, 10) };
        cancel.Click += (_, _) => Close(); buttons.Children.Add(cancel);
        footer.Children.Add(buttons);
        layout.Children.Add(footer);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        layout.Children.Add(scroll);
        var form = new StackPanel { Margin = new Thickness(0, 0, 8, 0) }; scroll.Content = form;
        form.Children.Add(new TextBlock { Text = "REPORT AN ISSUE", FontSize = 11, Foreground = Accent(), FontWeight = FontWeights.Bold });
        form.Children.Add(new TextBlock { Text = "Help improve Viora", FontSize = 27, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 8) });
        form.Children.Add(Note("Reports on GitHub are PUBLIC. Check the details for usernames, private paths, screenshots, secrets and personal information."));
        AddInput(form, "Issue title", _title, 38, "Short description of the problem");
        AddInput(form, "Steps to reproduce", _steps, 78, "1. Open Viora\n2. ...");
        AddInput(form, "Expected behaviour", _expected, 55, "What should happen?");
        AddInput(form, "Actual behaviour / error text", _actual, 72, "What happened? Include only non-sensitive error text.");
        _diagnostics.Content = "Include minimal technical diagnostics (version and Roblox detection)";
        _diagnostics.Margin = new Thickness(0, 8, 0, 15);
        _diagnostics.Checked += (_, _) => RefreshPreview();
        _diagnostics.Unchecked += (_, _) => RefreshPreview();
        form.Children.Add(_diagnostics);
        form.Children.Add(new TextBlock { Text = "Review the exact text to copy", FontWeight = FontWeights.SemiBold, FontSize = 15 });
        _preview.AcceptsReturn = true; _preview.TextWrapping = TextWrapping.Wrap;
        _preview.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _preview.MinHeight = 170; _preview.MaxHeight = 280;
        _preview.FontFamily = new FontFamily("Consolas");
        _preview.Margin = new Thickness(0, 9, 0, 6);
        form.Children.Add(_preview);
        form.Children.Add(Note("You can edit the preview before copying. Viora opens a browser; you choose whether to submit the issue."));
        foreach (var textBox in new[] { _title, _steps, _expected, _actual })
            textBox.TextChanged += (_, _) => RefreshPreview();
        RefreshPreview();
    }

    private static Brush Accent() => new SolidColorBrush(Color.FromRgb(192, 157, 255));
    private static TextBlock Note(string text) => new()
    {
        Text = text, Foreground = new SolidColorBrush(Color.FromRgb(187, 173, 205)),
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 15)
    };
    private static void AddInput(StackPanel parent, string label, TextBox input, int height, string hint)
    {
        parent.Children.Add(new TextBlock
        {
            Text = label, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Gainsboro,
            Margin = new Thickness(0, 3, 0, 6)
        });
        input.Height = height;
        input.AcceptsReturn = height > 38;
        input.TextWrapping = TextWrapping.Wrap;
        input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        input.ToolTip = hint;
        input.Margin = new Thickness(0, 0, 0, 12);
        parent.Children.Add(input);
    }
    private void RefreshPreview()
    {
        string version = typeof(IssueReportWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        string details = _diagnostics.IsChecked == true
            ? $"\n### Minimal diagnostics\n- Viora: {version}\n- Platform: Windows\n- Roblox installed: {CheckRoblox()}\n"
            : "";
        _preview.Text = $"### Summary\n{_title.Text.Trim()}\n\n### Steps to reproduce\n{_steps.Text.Trim()}\n\n### Expected\n{_expected.Text.Trim()}\n\n### Actual\n{_actual.Text.Trim()}\n" + details;
    }
    private static string CheckRoblox()
    {
        try { return RobloxService.FindInstallations().Count > 0 ? "Yes" : "No"; }
        catch { return "Unknown"; }
    }
    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_title.Text) || string.IsNullOrWhiteSpace(_actual.Text))
        {
            MessageBox.Show(this, "Please provide a title and describe what happened.", "Viora", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var answer = MessageBox.Show(this,
            "The text shown in the preview will be copied to your clipboard and GitHub Issues will open in your browser.\n\n" +
            "GitHub reports are public. Check for personal details before posting. Nothing is submitted automatically. Continue?",
            "Review before sharing", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            Clipboard.SetText(_preview.Text);
            Process.Start(new ProcessStartInfo("https://github.com/balcony3232/Viora/issues/new") { UseShellExecute = true });
            MessageBox.Show(this, "Your reviewed text was copied. Paste it into the GitHub issue form, then review it once more before submitting.", "Viora", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not copy the report or open GitHub: " + ex.Message, "Viora", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
