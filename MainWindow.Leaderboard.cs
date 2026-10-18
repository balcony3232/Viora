using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace Viora;

public partial class MainWindow
{
    private VioraPersonalRank? _myOnlineRank;

    private Brush RankColour(int rank) => rank switch
    {
        1 => (Brush)FindResource("Accent"),
        2 => new SolidColorBrush(Color.FromRgb(192, 189, 214)),
        3 => new SolidColorBrush(Color.FromRgb(195, 142, 112)),
        _ => (Brush)FindResource("Fg")
    };

    private TextBlock RankText(string text, int size = 13, bool strong = false,
        Brush? colour = null) => new()
    {
        Text = text, FontSize = size, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = colour ?? (Brush)FindResource("Fg"),
        HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxWidth = 230
    };

    private UIElement PlayerAvatar(long id, string username, int version, double size)
    {
        var holder = new Grid { Width = size, Height = size };
        holder.Children.Add(new Ellipse
        {
            Fill = (Brush)FindResource("PanelRaised"),
            Stroke = (Brush)FindResource("Accent"),
            StrokeThickness = 1.5
        });
        holder.Children.Add(new TextBlock
        {
            Text = username.Length == 0 ? "?" : username[..1].ToUpperInvariant(),
            FontSize = size * .40, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("Accent")
        });

        Uri? url = _onlineService?.AvatarUri(id, version);
        if (url is not null)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = url;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();
                // The initials and base circle remain behind the image if loading fails.
                holder.Children.Add(new Ellipse
                {
                    Fill = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill },
                    Stroke = (Brush)FindResource("Accent"),
                    StrokeThickness = 1.5
                });
            }
            catch (Exception) { /* Broken/unreachable avatar: initials fallback. */ }
        }
        return holder;
    }

    private Border CreatePodiumCard(int place, VioraLeaderboardEntry? person)
    {
        double height = place switch { 1 => 286, 2 => 240, _ => 226 };
        var border = new Border
        {
            MinHeight = height,
            Padding = new Thickness(12, 15, 12, 13),
            CornerRadius = new CornerRadius(16, 16, 9, 9),
            Background = (Brush)FindResource(place == 1 ? "PanelRaised" : "Panel"),
            BorderBrush = RankColour(place),
            BorderThickness = new Thickness(place == 1 ? 2 : 1),
            Margin = new Thickness(0, 2, 0, 0)
        };
        var body = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };

        var rank = RankText(place switch { 1 => "★ CHAMPION", 2 => "◆ RUNNER-UP", _ => "◆ THIRD PLACE" },
            11, true, RankColour(place));
        rank.Margin = new Thickness(0, 0, 0, 11);
        body.Children.Add(rank);

        var avatar = PlayerAvatar(person?.PublicId ?? 0, person?.Username ?? "", person?.AvatarVersion ?? 0,
            place == 1 ? 82 : 65);
        if (avatar is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Center;
        body.Children.Add(avatar);

        var medal = RankText("#" + place, place == 1 ? 29 : 23, true, RankColour(place));
        medal.Margin = new Thickness(0, 7, 0, 4);
        body.Children.Add(medal);
        var name = RankText(person?.Username ?? "Waiting for player", place == 1 ? 17 : 15, true);
        body.Children.Add(name);
        if (person is not null)
        {
            var tag = RankText(person.RoleLabel + "  ·  " + person.IdLabel, 11,
                colour: (Brush)FindResource("Muted"));
            tag.Margin = new Thickness(0, 4, 0, 10);
            body.Children.Add(tag);
            body.Children.Add(RankText(person.PlaytimeLabel, place == 1 ? 18 : 16, true));
        }
        else
        {
            body.Children.Add(RankText("No ranked player yet", 11, colour: (Brush)FindResource("Muted")));
        }
        border.Child = body;
        return border;
    }

    private Border CreateLeaderboardRow(VioraLeaderboardEntry p)
    {
        bool mine = _onlineProfile?.PublicId == p.PublicId;
        var panel = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = (Brush)FindResource(mine ? "PanelRaised" : "Panel"),
            BorderBrush = (Brush)FindResource(mine ? "Accent" : "Stroke"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(11, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 6)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(142) });
        grid.Children.Add(new TextBlock
        {
            Text = p.RankLabel, Foreground = RankColour((int)p.Rank),
            FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
        });
        var avatar = PlayerAvatar(p.PublicId, p.Username, p.AvatarVersion, 34);
        Grid.SetColumn(avatar, 1);
        grid.Children.Add(avatar);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock
        {
            Text = p.Username + (mine ? "  ·  YOU" : ""),
            FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Fg"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        names.Children.Add(new TextBlock
        {
            Text = p.IdLabel + "  ·  " + p.RoleLabel,
            Foreground = (Brush)FindResource("Muted"), FontSize = 11
        });
        Grid.SetColumn(names, 2); grid.Children.Add(names);
        var playtime = new TextBlock
        {
            Text = p.PlaytimeLabel, Foreground = (Brush)FindResource("Fg"),
            FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(playtime, 3); grid.Children.Add(playtime);
        panel.Child = grid;
        return panel;
    }

    private void RenderLeaderboardV2(IReadOnlyList<VioraLeaderboardEntry> players)
    {
        PodiumSecondSlot.Content = CreatePodiumCard(2, players.FirstOrDefault(p => p.Rank == 2));
        PodiumFirstSlot.Content = CreatePodiumCard(1, players.FirstOrDefault(p => p.Rank == 1));
        PodiumThirdSlot.Content = CreatePodiumCard(3, players.FirstOrDefault(p => p.Rank == 3));
        LeaderboardRowsHost.Children.Clear();
        foreach (var player in players.Where(p => p.Rank > 3))
            LeaderboardRowsHost.Children.Add(CreateLeaderboardRow(player));

        OnlineLeaderboardStatus.Text = players.Count == 0
            ? "Nobody is sharing playtime yet. The podium will populate as people opt in."
            : players.Count <= 3 ? "Podium populated. More participating players will appear below."
            : $"Showing ranks #4–#{players.Last().Rank}. Ranked by server-estimated playtime.";
    }

    private void UpdateMyRank(VioraPersonalRank? rank)
    {
        _myOnlineRank = rank;
        LeaderboardYourRank.Text = rank is null ? "Ranking unavailable."
            : rank.OptedIn
                ? rank.RankLabel + "  ·  " + rank.PlaytimeSeconds / 3600 + "h " +
                  rank.PlaytimeSeconds / 60 % 60 + "m  ·  " + rank.Username
                : "Not participating. Check the box above if you'd like to join.";
    }

    private async Task OfferLeaderboardConsentAsync()
    {
        if (_onlineService is null || _onlineProfile is null || !_isSignedIn) return;
        try
        {
            if (await _onlineService.LeaderboardConsentSeenAsync()) return;
            var yes = MessageBox.Show(this,
                "Would you like to join Viora's public playtime leaderboard?\n\n" +
                "If you join, your Viora username, public User ID, assigned tag, " +
                "optional avatar and approximate Roblox playtime will be visible to other users. " +
                "Viora will send periodic Roblox-running signals while it is open.\n\n" +
                "You can decline and still use Viora, and you can leave later.",
                "Optional public leaderboard", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (!_isSignedIn || _onlineProfile is null || _onlineService?.IsSignedIn != true) return;
            _onlineProfile = await _onlineService.SetLeaderboardVisibilityAsync(yes == MessageBoxResult.Yes);
            RefreshOnlineAccountUI();
            if (_onlineProfile?.LeaderboardOptIn == true) _ = SendOnlinePlaytimePingAsync();
        }
        catch (Exception)
        {
            // Network issues must not opt in anyone, block local use, or silently
            // claim consent. The prompt may be retried on the next sign-in.
        }
    }

    private async void OnlineAvatarUpload_Click(object sender, RoutedEventArgs e)
    {
        if (_onlineService?.IsSignedIn != true || _onlineProfile is null) return;
        if (MessageBox.Show(this,
            "Profile pictures are OPTIONAL. Uploaded images are publicly accessible by URL, " +
            "even if you later leave the leaderboard. Only use a picture you're happy to share publicly.\n\n" +
            "Continue and choose an image?",
            "Public profile picture", MessageBoxButton.YesNo,
            MessageBoxImage.Information) != MessageBoxResult.Yes) return;
        var picker = new OpenFileDialog
        {
            Title = "Choose your Viora avatar",
            Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(picker.FileName).Length > 8 * 1024 * 1024)
                throw new ArgumentException("The image is larger than 8 MB.");
            byte[] png;
            using (var input = File.OpenRead(picker.FileName))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = input;
                bitmap.DecodePixelWidth = 192;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                // Re-encode decoded pixels only: do not upload filename, original
                // metadata, arbitrary user-supplied bytes or embedded EXIF.
                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(converted));
                using var output = new MemoryStream();
                encoder.Save(output);
                png = output.ToArray();
            }
            if (png.Length > 262144)
                throw new ArgumentException("Image is still too large after conversion. Try a simpler image.");
            OnlineAvatarStatus.Text = "Uploading your avatar…";
            var accountId = _onlineProfile.PublicId;
            await _onlineService.UploadAvatarAsync(accountId, png);
            if (_onlineProfile?.PublicId != accountId) return;
            OnlineAvatarStatus.Text = "Avatar uploaded. Updating the leaderboard…";
            await RefreshOnlineLeaderboardAsync();
            OnlineAvatarStatus.Text = "Avatar saved. Other users will see it when your account is opted in.";
        }
        catch (Exception ex)
        {
            OnlineAvatarStatus.Text = "Avatar upload failed: " + ex.Message;
        }
    }

    private void ClearLeaderboardV2()
    {
        PodiumSecondSlot.Content = null;
        PodiumFirstSlot.Content = null;
        PodiumThirdSlot.Content = null;
        LeaderboardRowsHost.Children.Clear();
        UpdateMyRank(null);
        OnlineAvatarStatus.Text = "Optional: use a profile picture or an initials avatar.";
    }
}
