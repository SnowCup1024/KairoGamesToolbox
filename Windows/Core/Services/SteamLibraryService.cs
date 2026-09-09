using Microsoft.Win32;

namespace KairosoftGameToolbox.Services;

public sealed record InstalledAppInfo(string InstallDir, string LibraryPath, long? LastPlayedUnix);

/// <summary>
/// Steam 库识别：
/// 注册表 SteamPath → steamapps/libraryfolders.vdf（所有库）→ 逐库 appmanifest_*.acf（已装游戏）。
/// 开罗判定：installdir 存在 KairoGames.exe + (KairoFramework.dll 或 KairoGames_Data)，AppID 表兜底。
/// </summary>
public sealed class SteamLibraryService
{
    public string? SteamPath { get; private set; }

    public IReadOnlyList<string> Libraries { get; private set; } = Array.Empty<string>();

    private readonly Dictionary<uint, InstalledAppInfo> _installed = new();

    /// <summary>所有已安装游戏：appid → 安装目录 + 库路径。</summary>
    public IReadOnlyDictionary<uint, InstalledAppInfo> InstalledApps => _installed;

    /// <summary>检测 Steam 安装根目录：设置覆盖优先，其次注册表 HKCU\Software\Valve\Steam\SteamPath。</summary>
    public string? DetectSteamPath(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && Directory.Exists(overridePath))
            return NormalizeDirectoryPath(overridePath);

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var v = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(v) && Directory.Exists(v)) return NormalizeDirectoryPath(v);
        }
        catch
        {
            // 注册表不可读时忽略
        }
        return null;
    }

    /// <summary>统一分隔符，并按磁盘中的实际目录名称恢复大小写；不强制修改目录名大小写。</summary>
    public static string NormalizeDirectoryPath(string path)
    {
        var input = path.Trim();
        try
        {
            var full = Path.GetFullPath(input);
            var root = Path.GetPathRoot(full)!;
            if (OperatingSystem.IsWindows() && root.Length >= 2 && root[1] == ':')
                root = char.ToUpperInvariant(root[0]) + root[1..];
            var current = root;
            foreach (var part in full[root.Length..].Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries))
            {
                var names = Directory.GetDirectories(current);
                var match = names.FirstOrDefault(entry => Path.GetFileName(entry) == part)
                    ?? names.FirstOrDefault(entry => string.Equals(Path.GetFileName(entry), part,
                        StringComparison.OrdinalIgnoreCase));
                current = Path.Combine(current, match == null ? part : Path.GetFileName(match));
            }
            return current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return input;
        }
    }

    /// <summary>重新扫描：解析库与已安装游戏。失败时保留空结果（UI 显示空态引导）。</summary>
    public void Scan(string? steamPathOverride)
        => Scan(steamPathOverride, CancellationToken.None);

    /// <summary>可取消的扫描版本，供启动器后台刷新使用。</summary>
    public void Scan(string? steamPathOverride, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SteamPath = DetectSteamPath(steamPathOverride);
        _installed.Clear();
        var libs = new List<string>();

        if (SteamPath != null)
        {
            var vdfPath = Path.Combine(SteamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdfPath))
            {
                try
                {
                    var vdf = VdfParser.Parse(File.ReadAllText(vdfPath));
                    if (vdf.TryGetValue("libraryfolders", out var lf) && lf is Dictionary<string, object> folders)
                    {
                        foreach (var (_, val) in folders)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (val is Dictionary<string, object> folder
                                && folder.TryGetValue("path", out var p) && p is string sp)
                            {
                                libs.Add(sp);
                            }
                        }
                    }
                }
                catch
                {
                    // 解析失败走主库兜底
                }
            }
            if (libs.Count == 0) libs.Add(SteamPath);
        }

        foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var steamapps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(steamapps)) continue;

            IEnumerable<string> acfs;
            try { acfs = Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"); }
            catch { continue; }

            foreach (var acf in acfs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var vdf = VdfParser.Parse(File.ReadAllText(acf));
                    if (!vdf.TryGetValue("AppState", out var st) || st is not Dictionary<string, object> appState) continue;
                    if (!(appState.TryGetValue("appid", out var aid) && uint.TryParse(aid as string, out var appId))) continue;
                    if (!(appState.TryGetValue("installdir", out var idir) && idir is string installdir)) continue;
                    long? lastPlayed = null;
                    if ((appState.TryGetValue("lastplayed", out var lp) || appState.TryGetValue("LastPlayed", out lp))
                        && lp is string lps && long.TryParse(lps, out var parsedLastPlayed) && parsedLastPlayed > 0)
                    {
                        lastPlayed = parsedLastPlayed;
                    }
                    _installed[appId] = new InstalledAppInfo(
                        Path.Combine(steamapps, "common", installdir),
                        lib,
                        lastPlayed);
                }
                catch
                {
                    // 单个 acf 损坏不影响其余
                }
            }
        }

        Libraries = libs;
    }

    /// <summary>文件检测：KairoGames.exe + (KairoFramework.dll | KairoGames_Data)。</summary>
    public static bool IsKairoDir(string installDir)
    {
        try
        {
            return File.Exists(Path.Combine(installDir, "KairoGames.exe"))
                && (File.Exists(Path.Combine(installDir, "KairoFramework.dll"))
                    || Directory.Exists(Path.Combine(installDir, "KairoGames_Data")));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>已安装游戏中的开罗游戏：文件检测为主，AppID 表兜底。</summary>
    public Dictionary<uint, InstalledAppInfo> KairoInstalledApps(AppIdCatalog catalog)
    {
        var result = new Dictionary<uint, InstalledAppInfo>();
        foreach (var (appId, info) in _installed)
        {
            if (IsKairoDir(info.InstallDir) || catalog.Contains(appId))
                result[appId] = info;
        }
        return result;
    }
}
