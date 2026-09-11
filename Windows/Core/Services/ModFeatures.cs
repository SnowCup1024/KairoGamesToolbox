namespace KairosoftGameToolbox.Services;

public sealed record ModFeature(string Id, string Name, string Description);

public static class ModFeatures
{
    public static IReadOnlyList<ModFeature> ForGame(uint appId) => appId == 2934180
        ? new[] { new ModFeature("moneyReverse", "金钱反加", "将实际扣款转为等额增加，正常收入不变。") }
        : Array.Empty<ModFeature>();

    public static bool CanConnect(uint appId, string? directory) => ForGame(appId).Count > 0
        && GameFolderService.ContainsExecutable(directory) && ModPackageService.HasInstalledMod(directory!)
        && File.Exists(Path.Combine(directory!, "BepInEx/plugins/KairoMods.Observer/KairoMods.Observer.dll"));

    public static int Columns(int count, double width)
    {
        if (count <= 0) return 1;
        int preferred = (int)Math.Ceiling(Math.Sqrt(count * 4.0 / 3.0));
        int fitting = double.IsFinite(width) ? Math.Max(1, (int)Math.Floor((width + 12) / 232)) : 1;
        return Math.Max(1, Math.Min(count, Math.Min(preferred, fitting)));
    }
}
