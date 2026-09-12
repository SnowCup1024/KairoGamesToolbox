using KairoMods.Protocol;

namespace KairoMods.DreamTownIsland;

internal sealed class ControlState
{
    public static readonly string[] FeatureIds = { "moneyReverse", "pointReverse", "buildingReverse", "itemReverse" };
    private Dictionary<string, FeatureState> states = FeatureIds.ToDictionary(id => id, _ => new FeatureState());
    public Dictionary<string, FeatureState> Snapshot() => new(states);
    public FeatureState Get(string id) => states[id];
    public string? Configure(Dictionary<string, FeatureState>? desired)
    {
        if (desired == null || desired.Count != FeatureIds.Length || desired.Any(p => !states.ContainsKey(p.Key)
            || p.Value == null || p.Value.Multiplier is not (0 or 1 or 20))) return "Invalid feature settings";
        states = new(desired);
        return null;
    }
    public static string? PointFeature(int id) => id switch
    {
        0 or 1 or 2 or 3 => "pointReverse", _ => null
    };

    public static long Refund(long before, long after, long requested, FeatureState setting, long maximum)
    {
        if (!setting.Enabled || setting.Multiplier is not (0 or 1 or 20) || requested >= 0
            || before < 0 || after < 0 || maximum <= 0 || before > maximum || after > maximum) return 0;
        decimal spent = (decimal)before - after;
        decimal refund = spent * (setting.Multiplier + 1m);
        if (spent <= 0 || refund > maximum || (decimal)after + refund > maximum) return 0;
        return (long)refund;
    }
}
