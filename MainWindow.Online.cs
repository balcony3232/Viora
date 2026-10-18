using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Viora;

public partial class MainWindow
{
    private VioraOnlineService? _onlineService;
    private VioraOnlineProfile? _onlineProfile;
    private bool _onlineRegisterMode;
    private bool _onlineClaimUsername;
    private bool _onlineLegacyMode;
    private bool _onlineUiRefreshing;
    private bool _onlineAuthBusy;
    private bool _onlinePingBusy;
    private DispatcherTimer? _onlinePingTimer;

    private bool OnlineLoginActive => _onlineService is not null && !_onlineLegacyMode;

    private void InitializeOnlineUI()
    {
        try { _onlineService = VioraOnlineService.FromEmbeddedConfig(); }
        catch (Exception) { _onlineService = null; /* Invalid deployment configuration: offline legacy only. */ }
        Closed += (_, _) =>
        {
            _onlinePingTimer?.Stop();
            _onlineService?.Dispose();
        };
    }

    private void RefreshOnlineAuthenticationScreen()
    {
        AuthOverlay.Visibility = Visibility.Visible;
        AuthEmailGroup.Visibility = Visibility.Visible;
        AuthRegisterMode.Visibility = _onlineClaimUsername ? Visibility.Collapsed : Visibility.Visible;
        AuthLegacyMode.Visibility = LocalAccountService.Exists ? Visibility.Visible : Visibility.Collapsed;
        AuthLegacyMode.Content = "Use existing local account (offline)";
        AuthGuest.Visibility = Visibility.Collapsed;
        AuthGuestHint.Visibility = Visibility.Collapsed;
        AuthUsernameGroup.Visibility = _onlineRegisterMode || _onlineClaimUsername
            ? Visibility.Visible : Visibility.Collapsed;
        AuthConfirmPanel.Visibility = _onlineRegisterMode ? Visibility.Visible : Visibility.Collapsed;
        AuthPassword.Visibility = _onlineClaimUsername ? Visibility.Collapsed : Visibility.Visible;
        AuthPasswordLabel.Visibility = _onlineClaimUsername ? Visibility.Collapsed : Visibility.Visible;
        AuthTitle.Text = _onlineClaimUsername ? "Choose your unique username"
            : _onlineRegisterMode ? "Create Viora Online account" : "Sign in to Viora Online";
        AuthSubtitle.Text = _onlineClaimUsername
            ? "Your email is verified. Claim a unique public username to finish setup."
            : _onlineRegisterMode ? "Your account works on other PCs. Confirm your email before signing in."
            : "Sign in with your Viora email and password.";
        AuthUsernameLabel.Text = "PUBLIC USERNAME";
        AuthAccountHint.Text = _onlineRegisterMode
            ? "Usernames are reserved when you first sign in after email verification, not when you submit registration."
            : _onlineClaimUsername ? "3–24 letters, numbers or underscores. Usernames cannot be changed in this preview."
            : "This is a Viora account, completely separate from your Roblox login.";
        AuthSubmit.Content = _onlineClaimUsername ? "Claim username"
            : _onlineRegisterMode ? "Register and send verification email" : "Sign in";
        AuthRegisterMode.Content = _onlineRegisterMode ? "Already have an account? Sign in"
            : "Create an online account";
        AuthPrivacyHint.Text = "Email/password authentication is handled by Supabase. " +
            "Local game settings stay on this PC. Sharing playtime publicly is optional.";
        AuthError.Text = "";
    }

    private void AuthRegisterMode_Click(object sender, RoutedEventArgs e)
    {
        if (_onlineAuthBusy || _isSignedIn || !OnlineLoginActive || _onlineClaimUsername) return;
        _onlineRegisterMode = !_onlineRegisterMode;
        AuthPassword.Clear();
        AuthConfirm.Clear();
        RefreshOnlineAuthenticationScreen();
    }

    private void AuthLegacyMode_Click(object sender, RoutedEventArgs e)
    {
        if (_onlineAuthBusy || _isSignedIn || _onlineService is null ||
            !LocalAccountService.Exists || _onlineClaimUsername) return;
        _onlineLegacyMode = !_onlineLegacyMode;
        _onlineRegisterMode = false;
        AuthPassword.Clear();
        AuthConfirm.Clear();
        RefreshAuthenticationScreen();
    }

    private async Task AuthOnlineSubmitAsync()
    {
        if (_onlineService is null || _onlineAuthBusy) return;
        _onlineAuthBusy = true;
        AuthSubmit.IsEnabled = false;
        AuthRegisterMode.IsEnabled = false;
        AuthLegacyMode.IsEnabled = false;
        try
        {
            if (_onlineClaimUsername)
            {
                var profile = await _onlineService.ClaimUsernameAsync(AuthUsername.Text.Trim());
                await CompleteOnlineSignInAsync(profile);
            }
            else if (_onlineRegisterMode)
            {
                var password = AuthPassword.Password;
                if (password != AuthConfirm.Password)
                    throw new ArgumentException("The passwords do not match.");
                await _onlineService.RegisterAsync(AuthUsername.Text, AuthEmail.Text, password);
                _onlineRegisterMode = false;
                AuthPassword.Clear();
                AuthConfirm.Clear();
                RefreshOnlineAuthenticationScreen();
                AuthError.Text = "Registration submitted. Check your email, follow the verification link, then sign in. " +
                                 "You'll claim your unique username after verification.";
            }
            else
            {
                var profile = await _onlineService.SignInAsync(AuthEmail.Text, AuthPassword.Password);
                AuthPassword.Clear();
                if (profile is null)
                {
                    _onlineClaimUsername = true;
                    AuthUsername.Text = _onlineService.SuggestedUsername;
                    RefreshOnlineAuthenticationScreen();
                    AuthError.Text = "Email verified. Choose or confirm your unique username.";
                }
                else await CompleteOnlineSignInAsync(profile);
            }
        }
        catch (Exception ex)
        {
            AuthError.Text = ex.Message;
            AuthPassword.Clear();
            AuthConfirm.Clear();
        }
        finally
        {
            _onlineAuthBusy = false;
            AuthSubmit.IsEnabled = true;
            AuthRegisterMode.IsEnabled = true;
            AuthLegacyMode.IsEnabled = true;
        }
    }

    private async Task CompleteOnlineSignInAsync(VioraOnlineProfile profile)
    {
        _onlineProfile = profile;
        _onlineClaimUsername = false;
        await FinishSignInAsync(profile.Username);
        RefreshOnlineAccountUI();
        _ = RefreshOnlineLeaderboardAsync();
    }

    private void RefreshOnlineAccountUI()
    {
        bool enabled = _onlineProfile is not null && _onlineService?.IsSignedIn == true;
        OnlineStatsPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled) return;
        var p = _onlineProfile!;
        OnlineAccountStatus.Text = p.Username + " · User #" + p.PublicId.ToString("D6") +
            " · " + p.Role + " · " + TimeSpan.FromSeconds(p.PlaytimeSeconds).TotalHours.ToString("0.0") +
            "h credited online";
        string tag = p.Role switch
        {
            "owner" => "  ◆ OWNER",
            "administrator" => "  ◆ ADMIN",
            "developer" => "  ◆ DEV",
            "tester" => "  ◆ TESTER",
            _ => ""
        };
        SidebarAccountLabel.Text = "●  " + p.Username + tag;
        SidebarUserId.Text = "User ID  #" + p.PublicId.ToString("D6");
        SidebarUserId.Visibility = Visibility.Visible;
        NavLeaderboard.Visibility = Visibility.Visible;
        NavOwnerPanel.Visibility = p.Role == "owner" ? Visibility.Visible : Visibility.Collapsed;
        if (p.Role != "owner" && _currentPage == "owner") SwitchPage("dashboard");
        _onlineUiRefreshing = true;
        try { OnlineLeaderboardVisible.IsChecked = p.LeaderboardOptIn; }
        finally { _onlineUiRefreshing = false; }
    }

    private async void OnlineLeaderboardVisible_Changed(object sender, RoutedEventArgs e)
    {
        if (_onlineUiRefreshing || _onlineService is null ||
            _onlineProfile is null || !_onlineService.IsSignedIn) return;
        bool desired = OnlineLeaderboardVisible.IsChecked == true;
        OnlineLeaderboardVisible.IsEnabled = false;
        try
        {
            _onlineProfile = await _onlineService.SetLeaderboardVisibilityAsync(desired);
            RefreshOnlineAccountUI();
            if (desired) _ = SendOnlinePlaytimePingAsync();
            await RefreshOnlineLeaderboardAsync();
        }
        catch (Exception ex)
        {
            OnlineLeaderboardStatus.Text = ex.Message;
            RefreshOnlineAccountUI();
        }
        finally { OnlineLeaderboardVisible.IsEnabled = true; }
    }

    private async void OnlineLeaderboardRefresh_Click(object sender, RoutedEventArgs e)
        => await RefreshOnlineLeaderboardAsync();

    private async Task RefreshOnlineLeaderboardAsync()
    {
        if (_onlineService is null || !_onlineService.IsSignedIn) return;
        try
        {
            var rows = await _onlineService.LeaderboardAsync();
            if (!_isSignedIn || _onlineProfile is null) return;
            OnlineLeaderboardList.ItemsSource = rows;
            OnlineLeaderboardStatus.Text = rows.Count == 0
                ? "No players have opted into the leaderboard yet."
                : "Top " + rows.Count + " opted-in players. Credited online time is approximate.";
            _onlineProfile = await _onlineService.MyProfileAsync();
            RefreshOnlineAccountUI();
        }
        catch (Exception ex) { OnlineLeaderboardStatus.Text = "Leaderboard unavailable: " + ex.Message; }
    }

    private async void OnlineAccountDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_onlineService?.IsSignedIn != true || _onlineProfile is null) return;
        if (MessageBox.Show(this,
            "Permanently delete your Viora Online account, public username, ID, role and online playtime? " +
            "This cannot be undone. Your local Viora settings will stay on this PC.",
            "Delete Viora Online account", MessageBoxButton.YesNo,
            MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var confirmation = new Window
        {
            Title = "Confirm permanent deletion", Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 375, Height = 180, ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)Application.Current.Resources["Panel"]
        };
        var layout = new StackPanel { Margin = new Thickness(18) };
        layout.Children.Add(new TextBlock { Text = "Type DELETE to confirm:", Margin = new Thickness(0, 0, 0, 9) });
        var entry = new TextBox { MaxLength = 6, Height = 32 };
        layout.Children.Add(entry);
        var button = new Button { Content = "Delete account", HorizontalAlignment = HorizontalAlignment.Right,
            Width = 130, Margin = new Thickness(0, 10, 0, 0) };
        button.Click += (_, _) =>
        {
            if (entry.Text == "DELETE") confirmation.DialogResult = true;
        };
        layout.Children.Add(button);
        confirmation.Content = layout;
        confirmation.Loaded += (_, _) => entry.Focus();
        if (confirmation.ShowDialog() != true) return;

        try
        {
            await _onlineService.DeleteAccountAsync();
            SignOut_Click(this, new RoutedEventArgs());
            SetStatus("Your online account was deleted. Local settings were preserved.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Online account deletion failed: " + ex.Message +
                "\nYour account may still exist. Do not assume it was deleted.",
                "Could not delete account", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StartOnlinePlaytimeMonitor()
    {
        if (_onlineProfile is null || _onlineService is null) return;
        _onlinePingTimer?.Stop();
        _onlinePingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _onlinePingTimer.Tick += (_, _) => { _ = SendOnlinePlaytimePingAsync(); };
        _onlinePingTimer.Start();
        _ = SendOnlinePlaytimePingAsync(); // initial signal; no time awarded for this ping
    }

    private async Task SendOnlinePlaytimePingAsync()
    {
        if (_onlinePingBusy || _onlineProfile?.LeaderboardOptIn != true ||
            _onlineService?.IsSignedIn != true || !_isSignedIn) return;
        _onlinePingBusy = true;
        try
        {
            // Only a boolean is sent. The database uses elapsed server time,
            // ignores client-supplied playtime and caps credit per call.
            await _onlineService.PingAsync(_sawRobloxRunning);
        }
        catch (Exception) { /* Retain offline monitoring even if online service is unreachable. */ }
        finally { _onlinePingBusy = false; }
    }

    private void StopOnlinePlaytimeMonitor()
    {
        _onlinePingTimer?.Stop();
        _onlinePingTimer = null;
        _onlinePingBusy = false;
    }

    private void SignOutOnlineAccount()
    {
        StopOnlinePlaytimeMonitor();
        _onlineProfile = null;
        OnlineLeaderboardList.ItemsSource = null;
        OnlineStatsPanel.Visibility = Visibility.Collapsed;
        SidebarUserId.Text = "";
        SidebarUserId.Visibility = Visibility.Collapsed;
        NavLeaderboard.Visibility = Visibility.Collapsed;
        NavOwnerPanel.Visibility = Visibility.Collapsed;
        _selectedOwnerAccount = null;
        OwnerSelectedAccount.Text = "Search for a user before changing their tag.";
        OwnerAssignButton.IsEnabled = false;
        if (_onlineService is not null) _ = _onlineService.SignOutAsync();
        _onlineClaimUsername = false;
    }
}
