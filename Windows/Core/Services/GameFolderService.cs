namespace KairosoftGameToolbox.Services;

public static class GameFolderService
{
    // 这里只检查基本入口；具体游戏及版本匹配由未来补丁实现。
    public static bool ContainsExecutable(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        try
        {
            return Path.IsPathFullyQualified(directory)
                && File.Exists(Path.Combine(directory, "KairoGames.exe"));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }
}
