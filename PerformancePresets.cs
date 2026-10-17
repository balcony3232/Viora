namespace Viora;

/// <summary>
/// Performance presets only touch entries from Roblox's original September 2025
/// published FastFlag allowlist. These are experimental rendering preferences, not
/// guaranteed performance upgrades. Frame-rate caps are managed separately in RobloxGraphicsSettings.
/// </summary>
public static class PerformancePresets
{
    public const string Max = "max";
    public const string Balanced = "balanced";
    public const string Default = "default";

    // Only these overrides are managed by the quick-presets feature.
    // Other profile settings are intentionally left alone.
    private static readonly string[] Managed =
    {
        "DFFlagTextureQualityOverrideEnabled", "DFIntTextureQualityOverride",
        "FIntDebugForceMSAASamples", "FIntFRMMaxGrassDistance", "FIntFRMMinGrassDistance"
    };

    public static bool IsManagedFlag(string name) => Managed.Contains(name, StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, string> Preview(string level, bool reduceGrass)
    {
        var chosen = new Dictionary<string, string>(StringComparer.Ordinal);
        switch (level)
        {
            case Max:
                chosen["DFFlagTextureQualityOverrideEnabled"] = "True";
                chosen["DFIntTextureQualityOverride"] = "0";
                chosen["FIntDebugForceMSAASamples"] = "1";
                break;
            case Balanced:
                chosen["DFFlagTextureQualityOverrideEnabled"] = "True";
                chosen["DFIntTextureQualityOverride"] = "1";
                chosen["FIntDebugForceMSAASamples"] = "2";
                break;
            case Default:
                break;
            default:
                throw new ArgumentException("Unknown performance preset.", nameof(level));
        }
        if (reduceGrass && level != Default)
        {
            chosen["FIntFRMMaxGrassDistance"] = "0";
            chosen["FIntFRMMinGrassDistance"] = "0";
        }
        return chosen;
    }

    public static void Apply(UserProfile profile, string level, bool reduceGrass, IReadOnlyCollection<string> publishedAllowlist)
    {
        var desired = Preview(level, reduceGrass);
        var allowed = new HashSet<string>(publishedAllowlist, StringComparer.Ordinal);
        if (desired.Keys.Any(key => !allowed.Contains(key)))
            throw new InvalidOperationException("A selected performance flag is missing from the built-in allowlist snapshot.");
        foreach (var name in Managed) profile.Flags.Remove(name);
        foreach (var item in desired) profile.Flags[item.Key] = item.Value;
        profile.PerformancePreset = level switch
        {
            Max => "Maximum performance" + (reduceGrass ? " + reduced grass" : ""),
            Balanced => "Balanced" + (reduceGrass ? " + reduced grass" : ""),
            _ => "Roblox defaults"
        };
    }

    public static void Clear(UserProfile profile)
    {
        foreach (var name in Managed) profile.Flags.Remove(name);
        profile.PerformancePreset = "Roblox defaults";
    }
}
