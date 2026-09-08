namespace KairosoftGameToolbox.Services;

/// <summary>已安装游戏的只读存档目录定位。</summary>
public static class SaveDirectoryService
{
    public static string? Find(string installDir, string? preferredSteamId)
    {
        var saves = Path.Combine(installDir, "saves");
        if (!Directory.Exists(saves)) return null;

        try
        {
            if (!string.IsNullOrWhiteSpace(preferredSteamId))
            {
                var preferred = Path.Combine(saves, preferredSteamId);
                if (IsSteamIdDirectory(preferred)) return preferred;
            }

            return Directory.EnumerateDirectories(saves)
                .FirstOrDefault(IsSteamIdDirectory);
        }
        catch
        {
            return null;
        }
    }

    public static bool IsSteamIdDirectory(string path)
        => Directory.Exists(path) && ulong.TryParse(Path.GetFileName(path), out _);
}
