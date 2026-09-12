using System.Text.Json;

namespace KairoMods.DreamTownIsland;

internal sealed class ObservationPlan
{
    public HookSpec[] Hooks { get; set; } = Array.Empty<HookSpec>();
    public SampleSpec[] Samples { get; set; } = Array.Empty<SampleSpec>();

    public static ObservationPlan Load()
    {
        using var stream = typeof(ObservationPlan).Assembly.GetManifestResourceStream("ObservationPlan.json")!;
        return JsonSerializer.Deserialize<ObservationPlan>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}

internal sealed class HookSpec
{
    public string Type { get; set; } = "";
    public string Method { get; set; } = "";
    public string[] Parameters { get; set; } = Array.Empty<string>();
    public string Returns { get; set; } = "System.Void";
    public bool Static { get; set; }
    public string? Reader { get; set; }
    public string Resource { get; set; } = "";
}

internal sealed class SampleSpec
{
    public string List { get; set; } = "";
    public string Type { get; set; } = "";
    public string[] Fields { get; set; } = Array.Empty<string>();
}
