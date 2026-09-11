using KairoMods.Protocol;

namespace KairosoftGameToolbox.Services;

// Process-only state: never serialized. Each game directory has independent settings.
public static class ModSessionCache
{
    private static readonly Dictionary<string, Dictionary<string, FeatureState>> values = new();
    public static Dictionary<string, FeatureState>? Get(uint appId, string directory)
        => values.TryGetValue(ModControlClient.PipeName(appId, directory), out var value) ? new(value) : null;
    public static void Set(uint appId, string directory, Dictionary<string, FeatureState> value)
        => values[ModControlClient.PipeName(appId, directory)] = new(value);
    public static void Clear(uint appId, string directory) => values.Remove(ModControlClient.PipeName(appId, directory));
}
