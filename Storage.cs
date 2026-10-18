using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Viora;

public static class Storage
{
    // First-run migration copies the older Violetstrap data; the original stays intact as backup.
    public static string Root { get; } = InitialiseRoot();

    private static string InitialiseRoot()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string destination = Path.Combine(local, "Viora");
        string legacy = Path.Combine(local, "Violetstrap");
        if (Directory.Exists(destination)) return destination;
        if (Directory.Exists(legacy))
        {
            // Stage then rename to avoid leaving a partially migrated app-data directory.
            string stage = Path.Combine(local, "Viora.migration-staging");
            try
            {
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
                CopyDirectoryWithoutLinks(legacy, stage);
                Directory.Move(stage, destination);
                return destination;
            }
            catch (IOException) { return legacy; }
            catch (UnauthorizedAccessException) { return legacy; }
        }
        Directory.CreateDirectory(destination);
        return destination;
    }

    private static void CopyDirectoryWithoutLinks(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }
        foreach (string subdir in Directory.EnumerateDirectories(source))
        {
            if ((File.GetAttributes(subdir) & FileAttributes.ReparsePoint) != 0) continue;
            CopyDirectoryWithoutLinks(subdir, Path.Combine(destination, Path.GetFileName(subdir)));
        }
    }
    public static string ModsDir => Path.Combine(Root, "mods");
    public static string BackupsDir => Path.Combine(Root, "backups");
    public static string AppliedDir => Path.Combine(Root, "applied");
    public static string StatePath => Path.Combine(Root, "settings.json");
    public static string ExtraCatalogPath => Path.Combine(Root, "catalog-extra.json");
    public static string AccountPath => Path.Combine(Root, "local-account.json");
    public static string BeforeV05SettingsBackup => Path.Combine(Root, "settings-before-v0.5.json");
    public static string SplashPath => Path.Combine(Root, "splash-background.png");
    public static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public static AppState Load()
    {
        Directory.CreateDirectory(Root);
        // Save an untouched migration backup once, before v0.5 might save newer fields.
        if (File.Exists(StatePath) && !File.Exists(BeforeV05SettingsBackup))
        {
            try { File.Copy(StatePath, BeforeV05SettingsBackup); }
            catch (IOException) { /* Best-effort backup. Do not block normal startup. */ }
            catch (UnauthorizedAccessException) { /* Existing settings still load normally. */ }
        }
        string? storedState = File.Exists(StatePath) ? File.ReadAllText(StatePath) : null;
        var state = storedState is null ? new AppState()
            : JsonSerializer.Deserialize<AppState>(storedState, JsonOptions) ?? new AppState();
        // Upgrades from 1.0.0 already have a workspace. Do not interrupt them with first-run setup.
        if (storedState is not null && !storedState.Contains("\"SetupWizardCompleted\"", StringComparison.Ordinal))
            state.SetupWizardCompleted = true;
        state.Updates ??= new();
        if (string.IsNullOrWhiteSpace(state.Updates.GitHubRepository) ||
            state.Updates.GitHubRepository.Equals("balcony3232/Violetstrap", StringComparison.OrdinalIgnoreCase))
            state.Updates.GitHubRepository = "balcony3232/Viora";
        state.LaunchShortcuts ??= new();
        state.CustomTheme ??= new();
        state.Profiles ??= new();
        state.Mods ??= new();
        state.Stats ??= new ActivityStats();
        state.Stats.DailyObservedSeconds ??= new();
        state.Stats.RecentSessions ??= new();
        state.Stats.Games ??= new();
        state.Stats.GameSessions ??= new();
        state.GamePresencePresets ??= new();
        foreach (var game in state.Stats.Games) game.DailySeconds ??= new();
        state.Discord ??= new DiscordOptions();
        if (state.Profiles.Count == 0) state.Profiles.Add(new UserProfile());
        if (!state.Profiles.Any(x => x.Id == state.ActiveProfileId)) state.ActiveProfileId = state.Profiles[0].Id;
        foreach (var p in state.Profiles) p.Flags ??= new();
        return state;
    }

    public static void Save(AppState state) => WriteJson(StatePath, state);
    public static void WriteJson<T>(string path, T data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data, JsonOptions));
        File.Move(temp, path, true);
    }

    public static List<FlagDefinition> LoadCatalog()
    {
        using var builtIn = typeof(Storage).Assembly.GetManifestResourceStream("Viora.Assets.FlagCatalog.json")
            ?? throw new FileNotFoundException("The embedded FastFlag catalogue could not be opened.");
        var officialSnapshot = JsonSerializer.Deserialize<List<FlagDefinition>>(builtIn, JsonOptions) ?? new();
        foreach (var flag in officialSnapshot) flag.IsAllowlisted = true;
        if (File.Exists(ExtraCatalogPath))
        {
            try
            {
                var extra = JsonSerializer.Deserialize<List<FlagDefinition>>(File.ReadAllText(ExtraCatalogPath), JsonOptions) ?? new();
                foreach (var flag in extra)
                {
                    if (string.IsNullOrWhiteSpace(flag.Name) || officialSnapshot.Any(x => x.Name.Equals(flag.Name, StringComparison.OrdinalIgnoreCase))) continue;
                    flag.IsAllowlisted = false; // imported data cannot claim official status
                    officialSnapshot.Add(flag);
                }
            }
            catch (JsonException) { /* Keep the built-in catalogue accessible. */ }
        }
        return officialSnapshot;
    }
}
