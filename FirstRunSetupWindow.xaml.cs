using System.Windows;
using System.Windows.Controls;

namespace Viora;

/// <summary>Local first-run preferences; no network calls or client writes occur here.</summary>
public partial class FirstRunSetupWindow : Window
{
    private int _step;
    private readonly UIElement[] _steps;
    private readonly string[] _titles = { "Welcome to Viora", "Roblox detection", "Appearance and performance", "Privacy preferences" };
    private readonly string[] _subtitles =
    {
        "A short introduction to the workspace.",
        "Locate an existing Roblox Player installation.",
        "Choose a theme and an optional profile preset.",
        "Select whether optional Discord activity should be enabled."
    };

    public string SelectedTheme => (ThemeChoice.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Violet";
    public string SelectedPerformancePreset => (PerformanceChoice.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "default";
    public bool EnableDiscordPresence => DiscordChoice.IsChecked == true;

    public FirstRunSetupWindow()
    {
        InitializeComponent();
        _steps = new UIElement[] { WelcomeStep, DetectionStep, PreferencesStep, PrivacyStep };
        ShowStep();
    }

    private void RefreshRobloxButton_Click(object sender, RoutedEventArgs e) => DetectRoblox();

    private void DetectRoblox()
    {
        try
        {
            int count = RobloxService.FindInstallations().Count;
            RobloxDetectionSummary.Text = count > 0
                ? $"{count} Roblox Player installation{(count == 1 ? "" : "s")} detected."
                : "Roblox Player was not detected. It can be installed later from the official Roblox website.";
        }
        catch
        {
            RobloxDetectionSummary.Text = "Roblox installation status is unavailable. Setup can continue.";
        }
    }

    private void ShowStep()
    {
        for (int i = 0; i < _steps.Length; i++)
            _steps[i].Visibility = i == _step ? Visibility.Visible : Visibility.Collapsed;
        StepTitle.Text = _titles[_step];
        StepSubtitle.Text = _subtitles[_step];
        StepCount.Text = $"{_step + 1} of {_steps.Length}";
        BackButton.IsEnabled = _step > 0;
        NextButton.Content = _step == _steps.Length - 1 ? "Finish setup" : "Continue";
        if (_step == 1) DetectRoblox();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_step > 0) { _step--; ShowStep(); }
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_step < _steps.Length - 1) { _step++; ShowStep(); return; }
        DialogResult = true;
    }
}