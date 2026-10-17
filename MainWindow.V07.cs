using System;
using System.Windows;
using System.Windows.Controls;

namespace Viora;

public partial class MainWindow
{
    // This is an observation indicator, not an assertion about Roblox's own internal state.
    private void UpdateGameTrackingIndicator()
    {
        if (GameHubTrackingStatus is null) return;
        GameHubTrackingStatus.Text = !_sawRobloxRunning
            ? "○ Roblox closed · game tracking paused"
            : _currentPlaceId is long id
                ? $"● Tracking game #{id} · updates every 5 seconds"
                : "◌ Roblox open · waiting for game identification";
    }

    private void DiscordTemplateText_Changed(object sender, TextChangedEventArgs e) => UpdatePresenceTemplatePreview();
    private void DiscordTemplateEnabled_Changed(object sender, RoutedEventArgs e) => UpdatePresenceTemplatePreview();

    private void DiscordTemplatePreset_Click(object sender, RoutedEventArgs e)
    {
        if (DiscordPerGameCombo.SelectedItem is not GameActivityRecord) return;
        switch ((sender as Button)?.Tag as string)
        {
            case "playing":
                DiscordPerGameDetails.Text = "Playing {game}";
                DiscordPerGameState.Text = "Having fun";
                break;
            case "grinding":
                DiscordPerGameDetails.Text = "Grinding in {game}";
                DiscordPerGameState.Text = "Working towards a goal";
                break;
            case "exploring":
                DiscordPerGameDetails.Text = "Exploring {game}";
                DiscordPerGameState.Text = "Discovering something new";
                break;
        }
        DiscordPerGameEnabled.IsChecked = true;
        UpdatePresenceTemplatePreview();
    }

    private void UpdatePresenceTemplatePreview()
    {
        // Some TextChanged events occur during InitializeComponent, before other controls exist.
        if (!_gameHubReady || DiscordTemplatePreviewDetails is null || DiscordTemplatePreviewState is null ||
            DiscordPerGameDetails is null || DiscordPerGameState is null || DiscordPerGameEnabled is null) return;
        if (DiscordPerGameCombo.SelectedItem is not GameActivityRecord game)
        {
            DiscordTemplatePreviewDetails.Text = "Choose a game above";
            DiscordTemplatePreviewState.Text = "Add a favourite or play a game first";
            return;
        }
        if (DiscordPerGameEnabled.IsChecked != true)
        {
            DiscordTemplatePreviewDetails.Text = "Default Roblox presence";
            DiscordTemplatePreviewState.Text = "This game's override is disabled";
            return;
        }
        DiscordTemplatePreviewDetails.Text = string.IsNullOrWhiteSpace(DiscordPerGameDetails.Text)
            ? "Playing " + game.DisplayName : ExpandGameTemplate(DiscordPerGameDetails.Text, game.DisplayName);
        DiscordTemplatePreviewState.Text = string.IsNullOrWhiteSpace(DiscordPerGameState.Text)
            ? "Playing / background / AFK (automatic)" : ExpandGameTemplate(DiscordPerGameState.Text, game.DisplayName);
    }
}
