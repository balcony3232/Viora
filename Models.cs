using System.Text.Json.Serialization;
using System.ComponentModel;
using System.Windows.Media;

namespace Viora;

public sealed class FlagDefinition
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Reference";
    public string Type { get; set; } = "Unknown";
    public string Description { get; set; } = "No verified description available.";
    public string ExampleValue { get; set; } = "";
    public string Warning { get; set; } = "";
    public string Confidence { get; set; } = "Unknown";
    public bool IsAllowlisted { get; set; } = false;
    [JsonIgnore] public string Status => IsAllowlisted ? "Published allowlist (2025)" : "Reference only";
}

public sealed class UserProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Default";
    public string PerformancePreset { get; set; } = "Not configured";
    public Dictionary<string, string> Flags { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ModEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "";
    public string SavedFile { get; set; } = "";
    public string RelativeTarget { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class ObservedSession
{
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset EndedUtc { get; set; }
    public long ObservedSeconds { get; set; }
    public string Experience { get; set; } = "";
    [JsonIgnore] public string StartedLocal => StartedUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm");
    [JsonIgnore] public string DurationLabel => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", (long)TimeSpan.FromSeconds(Math.Max(0, ObservedSeconds)).TotalHours, TimeSpan.FromSeconds(Math.Max(0, ObservedSeconds)).Minutes, TimeSpan.FromSeconds(Math.Max(0, ObservedSeconds)).Seconds);
    [JsonIgnore] public string ExperienceLabel => string.IsNullOrWhiteSpace(Experience) ? "Unidentified game" : Experience;
}

public sealed class ActivityStats
{
    public Dictionary<string, long> DailyObservedSeconds { get; set; } = new();
    public List<ObservedSession> RecentSessions { get; set; } = new();
    // New in v0.6. Older totals remain unchanged; we don't infer past game allocations.
    public List<GameActivityRecord> Games { get; set; } = new();
    public List<ObservedGameSession> GameSessions { get; set; } = new();
    // Tracks only the time during which Viora is running and can see RobloxPlayerBeta.
    public long ObservedPlaySeconds { get; set; }
    public int ObservedSessions { get; set; }
    public int LaunchRequests { get; set; }
    public long LongestObservedSessionSeconds { get; set; }
    public DateTimeOffset? LastObservedStartUtc { get; set; }
}

// v0.6: tracked per-experience activity, separate from legacy total Roblox runtime.
public sealed class GameActivityRecord : INotifyPropertyChanged
{
    // UI-only thumbnail; never persisted to settings.json.
    private ImageSource? _icon;
    [JsonIgnore] public ImageSource? Icon
    {
        get => _icon;
        set { if (ReferenceEquals(_icon, value)) return; _icon = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public long PlaceId { get; set; }
    public string Name { get; set; } = "";
    public long ObservedSeconds { get; set; }
    public int Sessions { get; set; }
    public DateTimeOffset? LastPlayedUtc { get; set; }
    public bool Favorite { get; set; }
    // v0.7: quick-launch clicks initiated from Viora (not confirmed Roblox joins).
    public int QuickLaunchRequests { get; set; }
    public DateTimeOffset? LastQuickLaunchUtc { get; set; }
    public Dictionary<string, long> DailySeconds { get; set; } = new();
    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"Place #{PlaceId}" : Name;
    [JsonIgnore] public string PlaytimeLabel => FormatDuration(ObservedSeconds);
    [JsonIgnore] public string LastPlayedLabel => LastPlayedUtc?.ToLocalTime().ToString("dd MMM yyyy, HH:mm") ?? "Not tracked yet";
    [JsonIgnore] public string FavoriteLabel => Favorite ? "★" : "☆";
    private static string FormatDuration(long seconds) => seconds >= 3600 ? $"{seconds/3600}h {(seconds%3600)/60}m" : $"{seconds/60}m {seconds%60}s";
}

public sealed class ObservedGameSession
{
    public long PlaceId { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset EndedUtc { get; set; }
    public long ObservedSeconds { get; set; }
    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"Place #{PlaceId}" : Name;
    [JsonIgnore] public string StartedLabel => StartedUtc.ToLocalTime().ToString("dd MMM, HH:mm");
    [JsonIgnore] public string DurationLabel => ObservedSeconds >= 3600 ? $"{ObservedSeconds/3600}h {(ObservedSeconds%3600)/60}m" : $"{ObservedSeconds/60}m {ObservedSeconds%60}s";
}

public sealed class GamePresencePreset
{
    public long PlaceId { get; set; }
    public string Details { get; set; } = ""; // Can include {game} placeholder.
    public string State { get; set; } = ""; // Can include {game} placeholder.
    public string LargeImageKey { get; set; } = "";
    // New option defaults on, so existing per-game settings remain active after upgrading.
    public bool Enabled { get; set; } = true;
}

public sealed class LocalAccount
{
    public string Username { get; set; } = "";
    public string PasswordSaltBase64 { get; set; } = "";
    public string PasswordHashBase64 { get; set; } = "";
    public bool StaySignedIn { get; set; }
}

public sealed class DiscordOptions
{
    public bool Enabled { get; set; } = false;
    public string ApplicationId { get; set; } = "";
    public bool ShowGameNames { get; set; } = true;
    public bool ShowAfk { get; set; } = true;
    public bool ShowWhenRobloxClosed { get; set; } = false;
    public bool DetectRobloxBrowsing { get; set; } = false;
    public int IdleMinutes { get; set; } = 5;
    public string LargeImageKey { get; set; } = "";
    public string IdleLabel { get; set; } = "Away from keyboard";
    public string ManualExperienceName { get; set; } = "";
}

public sealed class SplashOptions
{
    public bool Enabled { get; set; } = true;
    // Default, Image, Gif. Custom media is stored in Viora's private app folder.
    public string Mode { get; set; } = "Default";
    public string CustomMediaFile { get; set; } = "";
    public string BackgroundFile { get; set; } = "";
    public string Size { get; set; } = "Small";
    public double DurationSeconds { get; set; } = 2.0;
    public bool ShowLogo { get; set; } = true;
    public bool ShowText { get; set; } = true;
    public bool Fade { get; set; } = true;
    public int FadeInMilliseconds { get; set; } = 300;
    public int FadeOutMilliseconds { get; set; } = 300;
    public bool SkipIfRobloxRunning { get; set; } = false;

    public SplashOptions Clone() => (SplashOptions)MemberwiseClone();
}

public sealed class LaunchShortcut
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public long PlaceId { get; set; }
    public string Folder { get; set; } = "Favourites";
    public string ProfileId { get; set; } = "";
    // Set either to zero for no saved Roblox graphics changes.
    public int Fps { get; set; }
    public int Quality { get; set; } = -1;
    public int LaunchRequests { get; set; }
    public DateTimeOffset? LastLaunchedUtc { get; set; }
    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"Place #{PlaceId}" : Name;
    [JsonIgnore] public string Caption => $"{Folder}  •  {LaunchRequests} quick launches";
}

public sealed class UpdateOptions
{
    public string GitHubRepository { get; set; } = "balcony3232/Viora";
    public bool CheckAtStartup { get; set; } = true;
}

public sealed class ThemeColours
{
    public string Background { get; set; } = "#0B0912";
    public string Sidebar { get; set; } = "#100D1B";
    public string Panel { get; set; } = "#171321";
    public string Accent { get; set; } = "#AE83FF";
    public string Border { get; set; } = "#332847";
    public string Muted { get; set; } = "#A29CAF";
}

public sealed class AppState
{
    // Set after completing the optional first-run walkthrough.
    public bool SetupWizardCompleted { get; set; }
    public UpdateOptions Updates { get; set; } = new();
    public List<LaunchShortcut> LaunchShortcuts { get; set; } = new();
    public ThemeColours CustomTheme { get; set; } = new();
    public bool MinimizeToTray { get; set; } = true;
    public DiscordOptions Discord { get; set; } = new();
    public List<GamePresencePreset> GamePresencePresets { get; set; } = new();
    public string ActiveProfileId { get; set; } = "";
    public List<UserProfile> Profiles { get; set; } = new();
    public List<ModEntry> Mods { get; set; } = new();
    public string CustomIconFile { get; set; } = "";
    // Legacy path retained to migrate v0.5.1 custom splash backgrounds.
    public string SplashBackgroundFile { get; set; } = "";
    public SplashOptions Splash { get; set; } = new();
    public string AvatarImageFile { get; set; } = "";
    public string ColorTheme { get; set; } = "Violet";
    public bool ReduceMotion { get; set; } = false;
    // Glossy (default), Opaque, Glass. Glass uses layered UI translucency, not desktop acrylic.
    public string AppearanceStyle { get; set; } = "Glossy";
    public ActivityStats Stats { get; set; } = new();
}

public sealed class AppliedFlagLedger
{
    // A key with null means the value was missing before Viora changed it.
    public Dictionary<string, string?> OriginalValues { get; set; } = new(StringComparer.Ordinal);
}
