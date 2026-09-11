namespace KairoMods.Protocol;

public sealed record FeatureState(bool Enabled = false, int Multiplier = 1);
public sealed record ModControlRequest(int Protocol, string Action, Dictionary<string, FeatureState>? Features = null);
public sealed record ModControlResponse(int Protocol, uint AppId, string Directory, string Session,
    bool Ready, Dictionary<string, FeatureState> Features, string? Error = null);

public static class ControlProtocol
{
    public const int Version = 2;
    public const int MaxMessageBytes = 16384;
    public static bool ValidMultiplier(int value) => value is 1 or 2 or 5 or 20;
}
