// 启动器核心服务无头验证（不依赖 WinUI / 网络 / Steam 本体）。
// 覆盖：VDF 解析、KairosoftGames.json 目录、合成 Steam 库布局扫描、文件检测和联网封面地址解析。
// 运行：dotnet run --project Windows/Test
using KairosoftGameToolbox.Services;
using KairosoftGameToolbox.Models;
using System.Text.Json;

int failures = 0;

void Check(string name, bool cond)
{
    Console.WriteLine($"{(cond ? "PASS" : "FAIL")}  {name}");
    if (!cond) failures++;
}

// 1) VDF：多库 libraryfolders
var vdf = VdfParser.Parse("""
"libraryfolders"
{
	"0"
	{
		"path"		"D:\\SteamLibrary"
	}
	"1"
	{
		"path"		"E:\\SteamGames"
	}
}
""");
Check("libraryfolders 解析出 2 个库",
    vdf.TryGetValue("libraryfolders", out var lf) && lf is Dictionary<string, object> d && d.Count == 2);

// 1b) VDF：Windows 路径中的反斜杠必须原样保留（D:\SteamLibrary 而非 D:SteamLibrary）
var pathVdf = VdfParser.Parse("\"libraryfolders\" { \"0\" { \"path\" \"D:\\SteamLibrary\" } }");
var lib0 = ((Dictionary<string, object>)((Dictionary<string, object>)pathVdf["libraryfolders"])["0"]);
Check("VDF 反斜杠路径原样保留", (string)lib0["path"] == "D:\\SteamLibrary");

// 2) VDF：appmanifest 的 appid + installdir
var acf = VdfParser.Parse("""
"AppState"
{
	"appid"		"2191490"
	"installdir"		"Pocket Academy 3"
	"lastplayed"		"1700000000"
}
""");
var app = acf.TryGetValue("AppState", out var st) && st is Dictionary<string, object> parsedApp
    ? parsedApp
    : new Dictionary<string, object>();
Check("acf 解析 appid/installdir",
    app.TryGetValue("appid", out var parsedAppId) && parsedAppId as string == "2191490"
        && app.TryGetValue("installdir", out var parsedInstallDir) && parsedInstallDir as string == "Pocket Academy 3");
Check("acf 解析 lastplayed",
    app.TryGetValue("lastplayed", out var parsedLastPlayed) && parsedLastPlayed as string == "1700000000");

// 3) KairosoftGames.json 目录表
var catalog = new AppIdCatalog(Path.Combine(AppContext.BaseDirectory, "Data", "KairosoftGames.json"));
Check($"catalog 共 {catalog.Count} 款（应为 63）", catalog.Count == 63);
Check("锚点：Pocket Academy 3 = 2191490", catalog.TryGetName(2191490) == "Pocket Academy 3");
Check("锚点：Doraemon Dorayaki = 2934180", catalog.TryGetName(2934180) == "Doraemon Dorayaki Shop Story");
Check("目录 63 款均有 Steam 简体中文名",
    catalog.Entries.Count == 63 && catalog.Entries.All(entry => !string.IsNullOrWhiteSpace(entry.ChineseName)));
Check("锚点：Steam 简体中文名读取正确",
    catalog.TryGetChineseName(1952170) == "美食梦物语"
        && catalog.TryGetChineseName(2934180) == "哆啦A梦的铜锣烧店物语");

// 首次启动设置及路径大小写：只使用临时目录，不读取真实 Steam 注册表。
var startupRoot = Path.Combine(Path.GetTempPath(), "kairo_startup_" + Guid.NewGuid().ToString("N"));
try
{
    var steamDirectory = Path.Combine(startupRoot, "MiXeD", "Steam");
    Directory.CreateDirectory(steamDirectory);
    File.WriteAllText(Path.Combine(steamDirectory, "Steam.exe"), "");
    Directory.CreateDirectory(Path.Combine(steamDirectory, "steamapps"));
    var canonical = SteamLibraryService.NormalizeDirectoryPath(steamDirectory);
    var inputPath = OperatingSystem.IsWindows() ? steamDirectory.ToLowerInvariant().Replace('\\', '/') : steamDirectory;
    Check("路径还原磁盘目录大小写及分隔符",
        SteamLibraryService.NormalizeDirectoryPath(inputPath + "/") == canonical
        && canonical.EndsWith(Path.Combine("MiXeD", "Steam"), StringComparison.Ordinal));
    Check("用户路径识别统一格式", new SteamLibraryService().DetectSteamPath(inputPath) == canonical);
    var settingsPath = Path.Combine(startupRoot, "Config", "settings.json");
    var initial = new SettingsService(settingsPath);
    Check("首次启动自动定位并保存配置", initial.InitializeSteamPath(() => inputPath)
        && File.Exists(settingsPath) && new SettingsService(settingsPath).Current.SteamPathOverride == canonical);
    initial.Current.ThemePreference = "dark";
    initial.Save();
    var saved = File.ReadAllText(settingsPath);
    Check("再次启动保留用户路径且不重复写配置", initial.InitializeSteamPath(() => throw new Exception("不应检测"))
        && File.ReadAllText(settingsPath) == saved && initial.Current.ThemePreference == "dark");
    initial.Current.SteamPathOverride = inputPath;
    initial.Save();
    Check("已有配置的路径大小写在启动时修正并保存", initial.InitializeSteamPath(() => null)
        && new SettingsService(settingsPath).Current.SteamPathOverride == canonical);
    var missing = new SettingsService(Path.Combine(startupRoot, "NoSteam.json"));
    Check("未安装 Steam 也创建默认配置", missing.InitializeSteamPath(() => null)
        && File.Exists(Path.Combine(startupRoot, "NoSteam.json")) && missing.Current.SteamPathOverride == null);
    var blocked = new SettingsService(steamDirectory);
    Check("启动保存失败返回失败且保留内存设置", !blocked.InitializeSteamPath(() => canonical)
        && blocked.Current.SteamPathOverride == canonical);
}
finally
{
    Directory.Delete(startupRoot, recursive: true);
}

// 4) 合成 Steam 库布局：扫描 + 文件检测 + AppID 兜底
var root = Path.Combine(Path.GetTempPath(), "kairo_smoketest_" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "Steam.exe"), "");
    var steamapps = Path.Combine(root, "steamapps");
    var installDir = Path.Combine(steamapps, "common", "Pocket Academy 3");
    Directory.CreateDirectory(installDir);
    Directory.CreateDirectory(Path.Combine(installDir, "KairoGames_Data"));
    File.WriteAllText(Path.Combine(installDir, "KairoGames.exe"), "");
    File.WriteAllText(Path.Combine(steamapps, "libraryfolders.vdf"),
        $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{root}\" }} }}");
    File.WriteAllText(Path.Combine(steamapps, "appmanifest_2191490.acf"),
        "\"AppState\" { \"appid\" \"2191490\" \"installdir\" \"Pocket Academy 3\" \"lastplayed\" \"1700000000\" }");

    var steam = new SteamLibraryService();
    steam.Scan(root);
    Check("合成库扫描到 1 款已装游戏", steam.InstalledApps.Count == 1);
    Check("安装目录解析正确",
        steam.InstalledApps.TryGetValue(2191490, out var info)
        && info.InstallDir.EndsWith(Path.Combine("steamapps", "common", "Pocket Academy 3")));
    Check("安装清单读取最近游玩时间",
        info?.LastPlayedUnix == 1700000000);
    Check("文件检测命中开罗（KairoGames.exe + KairoGames_Data）",
        steam.KairoInstalledApps(catalog).ContainsKey(2191490));

    Check("Steam 安装根目录验证通过", SteamLibraryService.IsValidSteamPath(root));
    Check("Steam 路径拒绝普通目录及不存在目录", !SteamLibraryService.IsValidSteamPath(installDir)
        && !SteamLibraryService.IsValidSteamPath(Path.Combine(root, "missing")));
    Check("Steam 路径拒绝相对路径与空值", !SteamLibraryService.IsValidSteamPath(".")
        && !SteamLibraryService.IsValidSteamPath(null));
    Check("无效手动路径不静默回退注册表", new SteamLibraryService().DetectSteamPath(installDir) == null);
    Check("非 Steam 文件夹包含入口即可通过", GameFolderService.ContainsExecutable(installDir));
    Check("非 Steam 文件夹拒绝上级目录、EXE 路径及空值", !GameFolderService.ContainsExecutable(steamapps)
        && !GameFolderService.ContainsExecutable(Path.Combine(installDir, "KairoGames.exe"))
        && !GameFolderService.ContainsExecutable(null));
    Directory.CreateDirectory(Path.Combine(root, "config"));
    var loginPath = Path.Combine(root, "config", "loginusers.vdf");
    File.WriteAllText(loginPath, "\"Users\" { \"76561198000000001\" { \"MostRecent\" \"0\" } \"76561198000000002\" { \"MostRecent\" \"1\" } }");
    Check("账号识别兼容 Steam 字段大小写并选择最近账号", SteamAccountService.ResolveSteamId(steam) == "76561198000000002");
    File.WriteAllText(loginPath, "\"users\" { \"76561198000000001\" { \"mostrecent\" \"1\" } }");
    Check("账号识别兼容小写字段", SteamAccountService.ResolveSteamId(steam) == "76561198000000001");
    File.Delete(loginPath);
    Check("无登录记录及存档时无账号", SteamAccountService.ResolveSteamId(steam) == null);

    var saves = Path.Combine(installDir, "saves");
    Directory.CreateDirectory(Path.Combine(saves, "76561198000000001"));
    Directory.CreateDirectory(Path.Combine(saves, "76561198000000002"));
    Directory.CreateDirectory(Path.Combine(saves, "not-a-steamid"));
    Check("无登录记录时从存档兜底账号", SteamAccountService.ResolveSteamId(steam) is "76561198000000001" or "76561198000000002");
    Check("存档目录优先选择当前 SteamID",
        SaveDirectoryService.Find(installDir, "76561198000000002")
            == Path.Combine(saves, "76561198000000002"));
    Check("存档目录拒绝非数字目录",
        SaveDirectoryService.Find(installDir, "not-a-steamid")
            != Path.Combine(saves, "not-a-steamid"));

    Check("Steam 链接使用数值 AppID",
        SteamLinkService.Store(2191490) == "https://store.steampowered.com/app/2191490/"
            && SteamLinkService.Install(2191490) == "steam://install/2191490"
            && SteamLinkService.Run(2191490) == "steam://run/2191490");

    var cancelledScan = new CancellationTokenSource();
    cancelledScan.Cancel();
    var cancellationObserved = false;
    try
    {
        steam.Scan(root, cancelledScan.Token);
    }
    catch (OperationCanceledException)
    {
        cancellationObserved = true;
    }
    Check("Steam 扫描响应取消令牌", cancellationObserved);

    // 5) 兜底：目录表含但无开罗文件特征的游戏仍判为开罗
    var nonKairo = Path.Combine(steamapps, "common", "Some Other Game");
    Directory.CreateDirectory(nonKairo);
    File.WriteAllText(Path.Combine(steamapps, "appmanifest_1823710.acf"),
        "\"AppState\" { \"appid\" \"1823710\" \"installdir\" \"Some Other Game\" }");
    steam.Scan(root);
    Check("AppID 表兜底命中（无文件特征也判开罗）",
        steam.KairoInstalledApps(catalog).ContainsKey(1823710));
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

// 6) 临时文件夹中的文件检测；标准测试不依赖商业游戏副本。
var fixtureRoot = Path.Combine(Path.GetTempPath(), "kairo_file_fixture_" + Guid.NewGuid().ToString("N"));
try
{
    var doraemon = Path.Combine(fixtureRoot, "Doraemon");
    var pocket = Path.Combine(fixtureRoot, "PocketAcademy3");
    var nonKairo = Path.Combine(fixtureRoot, "NotKairo");
    Directory.CreateDirectory(Path.Combine(doraemon, "KairoGames_Data"));
    Directory.CreateDirectory(pocket);
    Directory.CreateDirectory(nonKairo);
    File.WriteAllText(Path.Combine(doraemon, "KairoGames.exe"), "");
    File.WriteAllText(Path.Combine(pocket, "KairoGames.exe"), "");
    File.WriteAllText(Path.Combine(pocket, "KairoFramework.dll"), "");
    Check("临时夹具命中文件检测（KairoGames_Data）",
        SteamLibraryService.IsKairoDir(doraemon));
    Check("临时夹具命中文件检测（KairoFramework.dll）",
        SteamLibraryService.IsKairoDir(pocket));
    Check("非开罗目录不误判", !SteamLibraryService.IsKairoDir(nonKairo));
}
finally
{
    if (Directory.Exists(fixtureRoot)) Directory.Delete(fixtureRoot, true);
}

var serializedSettings = JsonSerializer.Serialize(new LauncherSettings { SteamWebApiKey = "not-written-in-plain-text" });
using (var settingsDocument = JsonDocument.Parse(serializedSettings))
{
    Check("API Key JSON 不包含明文字段或明文值",
        !settingsDocument.RootElement.TryGetProperty("SteamWebApiKey", out _)
            && !serializedSettings.Contains("not-written-in-plain-text", StringComparison.Ordinal));
}

var settingsRoot = Path.Combine(Path.GetTempPath(), "kairo_settings_smoketest_" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(settingsRoot);
    var corruptFile = Path.Combine(settingsRoot, "corrupt.json");
    File.WriteAllText(corruptFile, "{\"SteamWebApiKeyProtected\":\"not-valid-dpapi\"}");
    var corrupt = new SettingsService(corruptFile);
    Check("损坏的 API Key 密文按未配置处理", corrupt.Current.SteamWebApiKey == null);
}
finally
{
    if (Directory.Exists(settingsRoot)) Directory.Delete(settingsRoot, true);
}

// 7) 三个分区独立排序：各区先按最近游玩时间倒序，再按英文名排序。
var grouped = GameOrdering.GroupAndOrder(new[]
{
    new KairoGame { AppId = 1, Name = "Zeta", IsInstalled = false, Ownership = OwnershipStatus.Unknown },
    new KairoGame { AppId = 2, Name = "Beta", IsInstalled = true, LastPlayedUnix = 100 },
    new KairoGame { AppId = 3, Name = "Alpha", IsInstalled = false, Ownership = OwnershipStatus.Owned },
    new KairoGame { AppId = 4, Name = "Gamma", IsInstalled = true, LastPlayedUnix = 200 },
    new KairoGame { AppId = 5, Name = "Delta", IsInstalled = false, Ownership = OwnershipStatus.NotOwned },
    new KairoGame { AppId = 6, Name = "Omega", IsInstalled = false, Ownership = OwnershipStatus.Owned, LastPlayedUnix = 300 },
    new KairoGame { AppId = 7, Name = "Theta", IsInstalled = false, Ownership = OwnershipStatus.NotOwned, LastPlayedUnix = 400 },
});
Check("分区：已安装、未安装、未拥有/未知互相独立",
    grouped.Count == 3
        && grouped[0].Kind == GameSectionKind.Installed
        && grouped[1].Kind == GameSectionKind.NotInstalled
        && grouped[2].Kind == GameSectionKind.NotOwnedOrUnknown
        && grouped[2].Title == "未拥有或状态未知");
Check("排序：已安装区按最近游玩倒序、无记录按名称",
    grouped[0].Games.Select(g => g.Name).SequenceEqual(new[] { "Gamma", "Beta" }));
Check("排序：未安装区按最近游玩倒序、无记录按名称",
    grouped[1].Games.Select(g => g.Name).SequenceEqual(new[] { "Omega", "Alpha" }));
Check("排序：未拥有/未知区按最近游玩倒序、无记录按名称",
    grouped[2].Games.Select(g => g.Name).SequenceEqual(new[] { "Theta", "Delta", "Zeta" }));
Check("排序：扁平结果保持分区顺序",
    GameOrdering.Order(grouped.SelectMany(section => section.Games)).Select(g => g.Name)
        .SequenceEqual(new[] { "Gamma", "Beta", "Omega", "Alpha", "Theta", "Delta", "Zeta" }));

// 8) 封面视觉状态：已安装原色，已拥有但未安装低饱和，未拥有/未知灰度。
Check("封面：已安装保持原始饱和度",
    new KairoGame { IsInstalled = true, Ownership = OwnershipStatus.Unknown }.CoverSaturation == 1.0);
Check("封面：已拥有但未安装使用低饱和",
    new KairoGame { IsInstalled = false, Ownership = OwnershipStatus.Owned }.CoverSaturation == 0.25);
Check("封面：未拥有/未知使用灰度",
    new KairoGame { IsInstalled = false, Ownership = OwnershipStatus.NotOwned }.CoverSaturation == 0.0
    && new KairoGame { IsInstalled = false, Ownership = OwnershipStatus.Unknown }.CoverSaturation == 0.0);
Check("状态：未拥有与无法确认拥有状态使用不同文案",
    new KairoGame { IsInstalled = false, Ownership = OwnershipStatus.NotOwned }.LibraryStatusText == "未拥有"
    && new KairoGame { IsInstalled = false, Ownership = OwnershipStatus.Unknown }.LibraryStatusText == "状态未知"
    && new KairoGame { IsInstalled = false, Ownership = OwnershipStatus.Owned }.LibraryStatusText == "已拥有"
    && new KairoGame { IsInstalled = true, Ownership = OwnershipStatus.Unknown }.LibraryStatusText == "已安装");

var bilingualSearchGame = new KairoGame
{
    Name = "美食梦物语",
    EnglishName = "Cafeteria Nipponica",
};
Check("搜索：支持中文、英文和忽略空格的模糊匹配",
    GameSearch.Matches(bilingualSearchGame, "美食梦")
        && GameSearch.Matches(bilingualSearchGame, "cafeteria")
        && GameSearch.Matches(bilingualSearchGame, "cafe nica")
        && !GameSearch.Matches(bilingualSearchGame, "冒险村"));

// 拼音由目录维护，避免多音字误读；新增目录条目必须补齐音节。
Check("搜索：63 款游戏均提供拼音并可用全拼及首字母找到",
    catalog.Entries.All(entry => !string.IsNullOrWhiteSpace(entry.PinyinName)
        && GameSearch.Matches(new KairoGame { PinyinName = entry.PinyinName }, entry.PinyinName)
        && GameSearch.Matches(new KairoGame { PinyinName = entry.PinyinName },
            string.Concat(entry.PinyinName.Split(' ').Select(part => part[0])))));
var pinyinGame = new KairoGame { Name = "口袋学院物语3", PinyinName = catalog.TryGetPinyinName(2191490) };
Check("搜索：全拼、大小写、分隔符与首字母", GameSearch.Matches(pinyinGame, "kou dai xue yuan")
    && GameSearch.Matches(pinyinGame, "KDX Y3") && GameSearch.Matches(pinyinGame, "kou-dai3"));
Check("搜索：允许省略部分拼音并保留数字", GameSearch.Matches(pinyinGame, "koudai3")
    && !GameSearch.Matches(pinyinGame, "koudai2") && !GameSearch.Matches(pinyinGame, "zzzz"));
var musicGame = new KairoGame { PinyinName = catalog.TryGetPinyinName(1952160) };
Check("搜索：乐曲使用 yue 读音", GameSearch.Matches(musicGame, "yuequ")
    && !GameSearch.Matches(musicGame, "lequ"));
Check("搜索：保留中英文混合名称与后缀",
    GameSearch.Matches(new KairoGame { PinyinName = catalog.TryGetPinyinName(2934180) }, "duolaameng")
    && GameSearch.Matches(new KairoGame { PinyinName = catalog.TryGetPinyinName(2072420) }, "kaituodx"));
Check("搜索：无拼音的目录外游戏仍支持英文，空查询显示全部",
    GameSearch.Matches(new KairoGame { EnglishName = "Unknown Game" }, "unknown")
    && GameSearch.Matches(pinyinGame, " ") && !GameSearch.Matches(new KairoGame(), "abc"));

// 联网封面使用合成 HTTP 响应测试，不连接 Steam。
string CoverMetadata(uint appId, string filename, string? format = null)
    => JsonSerializer.Serialize(new
    {
        response = new
        {
            store_items = new[]
            {
                new
                {
                    appid = appId,
                    success = 1,
                    assets = new
                    {
                        asset_url_format = format ?? $"steam/apps/{appId}/${{FILENAME}}?t=123",
                        library_capsule = "library_600x900.jpg",
                        library_capsule_2x = filename,
                    },
                },
            },
        },
    });

var classicMetadata = CoverMetadata(2191490, "library_600x900_2x.jpg");
var hashFilename = "b3e500ef923ebc345e073894f1411e4488e87475/library_capsule_2x.jpg";
var hashMetadata = CoverMetadata(4424950, hashFilename);
Check("封面：旧路径选择 2x JPG，避免下载 300×450 缩略图",
    SteamCoverClient.ParseCoverUri(classicMetadata, 2191490)?.AbsoluteUri
        == "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/2191490/library_600x900_2x.jpg?t=123");
Check("封面：新路径保留 library 自身 hash 和更新时间",
    SteamCoverClient.ParseCoverUri(hashMetadata, 4424950)?.AbsoluteUri
        == $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4424950/{hashFilename}?t=123");
Check("封面：拒绝其他游戏、损坏 JSON 与缺少资源的响应",
    SteamCoverClient.ParseCoverUri(hashMetadata, 2191490) == null
        && SteamCoverClient.ParseCoverUri("{", 2191490) == null
        && SteamCoverClient.ParseCoverUri("{\"response\":{}}", 2191490) == null
        && SteamCoverClient.ParseCoverUri("{\"response\":{\"store_items\":[null]}}", 2191490) == null);
Check("封面：拒绝外部 URL、路径穿越和非 JPG 资源",
    SteamCoverClient.ParseCoverUri(CoverMetadata(1, "library_capsule_2x.jpg", "https://example.com/${FILENAME}"), 1) == null
        && SteamCoverClient.ParseCoverUri(CoverMetadata(1, "../library_capsule_2x.jpg"), 1) == null
        && SteamCoverClient.ParseCoverUri(CoverMetadata(1, "library_capsule_2x.png"), 1) == null);

var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }; // 下载层只检查签名，界面层负责完整解码和尺寸检查。
var handler = new CoverHttpHandler(async (request, token) =>
{
    await Task.Delay(10, token);
    return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
    {
        Content = request.RequestUri!.Host == "api.steampowered.com"
            ? new StringContent(classicMetadata)
            : new ByteArrayContent(jpeg),
    };
});
using (var http = new HttpClient(handler))
{
    var covers = new SteamCoverClient(http);
    var downloads = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => covers.DownloadAsync(2191490)));
    Check("封面：并发同 AppID 只请求一次元数据和一次图片",
        handler.RequestCount == 2 && downloads.All(bytes => bytes?.SequenceEqual(jpeg) == true));
    Check("封面：重复读取复用内存缓存",
        (await covers.DownloadAsync(2191490))?.SequenceEqual(jpeg) == true && handler.RequestCount == 2);
}

foreach (var failureKind in new[] { "http", "network", "timeout", "html" })
{
    var failingHandler = new CoverHttpHandler((request, _) =>
    {
        if (failureKind == "network") throw new HttpRequestException("synthetic offline");
        if (failureKind == "timeout") throw new TaskCanceledException("synthetic timeout");
        return Task.FromResult(new HttpResponseMessage(failureKind == "http"
            ? System.Net.HttpStatusCode.ServiceUnavailable : System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.Host == "api.steampowered.com"
                ? classicMetadata : "<html>not an image</html>"),
        });
    });
    using var http = new HttpClient(failingHandler);
    var covers = new SteamCoverClient(http);
    Check($"封面：{failureKind} 失败降级为占位，不抛出异常", await covers.DownloadAsync(2191490) == null);
    var requestCount = failingHandler.RequestCount;
    Check($"封面：{failureKind} 失败短暂缓存，防止重复请求", await covers.DownloadAsync(2191490) == null
        && failingHandler.RequestCount == requestCount);
}

var currentSettingsRoot = Path.Combine(Path.GetTempPath(), "kairo_current_settings_" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(currentSettingsRoot);
    var file = Path.Combine(currentSettingsRoot, "settings.json");
    File.WriteAllText(file, "{\"SteamWebApiKey\":\"discard-this-key\",\"UseDarkTheme\":true}");
    var settings = new SettingsService(file);
    Check("设置：忽略明文 API Key 和已移除的主题字段",
        settings.Current.SteamWebApiKey == null && settings.Current.ThemePreference == "system");
    settings.Current.ThemePreference = "dark";
    settings.Current.SteamWebApiKey = "synthetic-current-key";
    Check("设置：当前 DPAPI 配置可保存并重新加载",
        settings.Save() && new SettingsService(file).Current is { ThemePreference: "dark", SteamWebApiKey: "synthetic-current-key" });
}
finally
{
    if (Directory.Exists(currentSettingsRoot)) Directory.Delete(currentSettingsRoot, true);
}

Check("用户数据：配置与封面统一使用 KairoGamesToolbox 目录",
    AppDataPaths.SettingsFile == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KairoGamesToolbox", "settings.json")
    && AppDataPaths.CoversDirectory == Path.Combine(AppDataPaths.Root, "Covers"));

var coverCacheRoot = Path.Combine(Path.GetTempPath(), "kairo_cover_cache_" + Guid.NewGuid().ToString("N"));
try
{
    var cachePath = Path.Combine(coverCacheRoot, "Covers");
    var cachedFile = Path.Combine(cachePath, "2191490.jpg");
    var diskHandler = new CoverHttpHandler((request, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
    {
        Content = request.RequestUri!.Host == "api.steampowered.com"
            ? new StringContent(classicMetadata) : new ByteArrayContent(jpeg),
    }));
    using var diskHttp = new HttpClient(diskHandler);
    var firstRun = new SteamCoverClient(diskHttp, cachePath);
    var parallelLoads = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => firstRun.DownloadAsync(2191490)));
    Check("磁盘封面：首次联网并原子写入 AppID 缓存，并发请求复用下载",
        parallelLoads.All(bytes => bytes?.SequenceEqual(jpeg) == true)
        && File.ReadAllBytes(cachedFile).SequenceEqual(jpeg) && diskHandler.RequestCount == 2
        && Directory.GetFiles(cachePath, "*.tmp").Length == 0);
    var restarted = new SteamCoverClient(diskHttp, cachePath);
    Check("磁盘封面：新服务实例模拟重启，缓存命中完全不联网",
        (await restarted.DownloadAsync(2191490))?.SequenceEqual(jpeg) == true && diskHandler.RequestCount == 2);

    File.WriteAllText(cachedFile, "broken image");
    Check("磁盘封面：损坏缓存重新下载并替换",
        (await new SteamCoverClient(diskHttp, cachePath).DownloadAsync(2191490))?.SequenceEqual(jpeg) == true
        && diskHandler.RequestCount == 4 && File.ReadAllBytes(cachedFile).SequenceEqual(jpeg));
    File.WriteAllBytes(cachedFile, new byte[] { 0xFF, 0xD8, 0xFF, 0x00 });
    var validating = new SteamCoverClient(diskHttp, cachePath,
        validateImage: bytes => Task.FromResult(bytes.SequenceEqual(jpeg)));
    Check("磁盘封面：JPEG 签名正确但解码校验失败仍重新获取",
        (await validating.DownloadAsync(2191490))?.SequenceEqual(jpeg) == true && diskHandler.RequestCount == 6);

    var steamRoot = Path.Combine(coverCacheRoot, "Steam");
    var steamCover = Path.Combine(steamRoot, "appcache", "librarycache", "2191490", "library_600x900.jpg");
    Directory.CreateDirectory(Path.GetDirectoryName(steamCover)!);
    File.WriteAllBytes(steamCover, jpeg);
    foreach (var reason in new[] { "http", "timeout", "metadata", "image" })
    {
        var fallbackDirectory = Path.Combine(coverCacheRoot, reason);
        var fallbackHandler = new CoverHttpHandler((request, _) =>
        {
            if (reason == "timeout") throw new TaskCanceledException("synthetic timeout");
            return Task.FromResult(new HttpResponseMessage(reason == "http"
                ? System.Net.HttpStatusCode.ServiceUnavailable : System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(reason == "image" && request.RequestUri!.Host == "api.steampowered.com"
                    ? classicMetadata : "{}"),
            });
        });
        using var fallbackHttp = new HttpClient(fallbackHandler);
        var fallback = new SteamCoverClient(fallbackHttp, fallbackDirectory,
            () => new[] { Path.Combine(coverCacheRoot, "missing-steam"), steamRoot });
        Check($"Steam 兜底：{reason} 失败后复制本地 library 封面，源文件不变",
            (await fallback.DownloadAsync(2191490))?.SequenceEqual(jpeg) == true
            && File.ReadAllBytes(Path.Combine(fallbackDirectory, "2191490.jpg")).SequenceEqual(jpeg)
            && File.ReadAllBytes(steamCover).SequenceEqual(jpeg) && fallbackHandler.RequestCount > 0);
        var previousRequests = fallbackHandler.RequestCount;
        Check($"Steam 兜底：{reason} 复制完成后重启只读 Covers",
            (await new SteamCoverClient(fallbackHttp, fallbackDirectory).DownloadAsync(2191490))?.SequenceEqual(jpeg) == true
            && fallbackHandler.RequestCount == previousRequests);
    }

    var unavailable = new CoverHttpHandler((_, _) => throw new HttpRequestException("offline"));
    using var unavailableHttp = new HttpClient(unavailable);
    var emptyCache = Path.Combine(coverCacheRoot, "empty");
    Check("Steam 兜底：网络和本地图片均缺失时返回占位，不生成空缓存",
        await new SteamCoverClient(unavailableHttp, emptyCache, () => new[] { steamRoot }).DownloadAsync(4424950) == null
        && !File.Exists(Path.Combine(emptyCache, "4424950.jpg")));
    File.WriteAllText(steamCover, "broken fallback");
    Check("Steam 兜底：损坏源图片不写入 Covers",
        await new SteamCoverClient(unavailableHttp, emptyCache, () => new[] { steamRoot }).DownloadAsync(2191490) == null
        && !File.Exists(Path.Combine(emptyCache, "2191490.jpg")));
    var blockedCache = Path.Combine(coverCacheRoot, "not-a-directory");
    File.WriteAllText(blockedCache, "keep this file");
    Check("磁盘封面：缓存目录无法写入时仍返回下载图片，不改动阻挡文件",
        (await new SteamCoverClient(diskHttp, blockedCache).DownloadAsync(2191490))?.SequenceEqual(jpeg) == true
        && File.ReadAllText(blockedCache) == "keep this file");
}
finally
{
    if (Directory.Exists(coverCacheRoot)) Directory.Delete(coverCacheRoot, true);
}

LauncherInstanceChecks.Run(Check);
ModFeatureChecks.Run(Check);
NonSteamChecks.Run(Check);
ModPackageChecks.Run(Check);
await ModControlChecks.RunAsync(Check);
BetaChecks.Run(Check);

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures;

sealed class CoverHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;
    private int _requestCount;

    public int RequestCount => _requestCount;

    public CoverHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        => _respond = respond;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCount);
        return _respond(request, cancellationToken);
    }
}
