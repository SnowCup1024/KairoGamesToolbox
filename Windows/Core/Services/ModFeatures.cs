namespace KairosoftGameToolbox.Services;

public sealed record ModFeature(string Id, string Name, string Description);

public static class ModFeatures
{
    public static IReadOnlyList<ModFeature> ForGame(uint appId) => BundledModService.ForGame(appId)?.Features
        .Select(f => new ModFeature(f.Id, BundledModService.Text(f.Names, L.Language), BundledModService.Text(f.Descriptions, L.Language))).ToList()
        ?? new List<ModFeature>();
    public static bool CanConnect(uint appId, string? directory)
    {
        var definition = BundledModService.ForGame(appId);
        return definition is { Files.Count: > 0 }
            && GameFolderService.ContainsExecutable(directory) && ModPackageService.HasInstalledMod(directory!)
            && definition.Files.All(file => File.Exists(ModPackageService.SafePath(directory!, file.Path)));
    }
    public static int Columns(int count, double width) => count > 1 && double.IsFinite(width) && width >= 880 ? 2 : 1;
}
