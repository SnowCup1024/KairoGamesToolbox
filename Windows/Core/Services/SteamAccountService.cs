namespace KairosoftGameToolbox.Services;

/// <summary>存档定位与游玩记录共用的 Steam 账号识别。</summary>
public static class SteamAccountService
{
    /// <summary>自动解析 SteamID64：loginusers.vdf 的 mostrecent 优先，其次已装游戏存档目录。</summary>
    public static string? ResolveSteamId(SteamLibraryService steam)
    {
        var steamPath = steam.SteamPath;
        if (!string.IsNullOrEmpty(steamPath))
        {
            var loginFile = Path.Combine(steamPath, "config", "loginusers.vdf");
            if (File.Exists(loginFile))
            {
                try
                {
                    var vdf = VdfParser.Parse(File.ReadAllText(loginFile));
                    var users = TryGetValue(vdf, "users", out var usersValue)
                        && usersValue is Dictionary<string, object> nestedUsers
                        ? nestedUsers
                        : vdf;
                    string? bestId = null;
                    int best = -1;
                    foreach (var (key, val) in users)
                    {
                        if (val is not Dictionary<string, object> acc || !ulong.TryParse(key, out _)) continue;
                        if (TryGetValue(acc, "mostrecent", out var mr) && mr is string mrs
                            && int.TryParse(mrs, out var mri) && mri > best)
                        {
                            best = mri;
                            bestId = key;
                        }
                    }
                    if (bestId != null) return bestId;
                }
                catch
                {
                    // 解析失败走存档目录兜底
                }
            }
        }

        // 兜底：已装游戏 saves/<SteamID> 目录
        foreach (var info in steam.InstalledApps.Values)
        {
            var saves = Path.Combine(info.InstallDir, "saves");
            if (!Directory.Exists(saves)) continue;
            try
            {
                foreach (var d in Directory.EnumerateDirectories(saves))
                {
                    var name = Path.GetFileName(d);
                    if (ulong.TryParse(name, out _)) return name;
                }
            }
            catch
            {
                // 继续下一个
            }
        }
        return null;
    }

    private static bool TryGetValue(Dictionary<string, object> values, string key, out object? value)
    {
        value = values.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
        return value != null;
    }
}
