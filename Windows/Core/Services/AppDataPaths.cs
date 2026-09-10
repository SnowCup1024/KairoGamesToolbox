namespace KairosoftGameToolbox.Services;

/// <summary>当前应用的用户数据位置；不读取或迁移其他目录。</summary>
public static class AppDataPaths
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KairoGamesToolbox");

    public static string ModsDirectory => Path.Combine(Root, "Mods");
    public static string NonSteamLibraryFile => Path.Combine(Root, "non-steam-games.json");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string CoversDirectory => Path.Combine(Root, "Covers");
}
