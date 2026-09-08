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

// 4) 合成 Steam 库布局：扫描 + 文件检测 + AppID 兜底
var root = Path.Combine(Path.GetTempPath(), "kairo_smoketest_" + Guid.NewGuid().ToString("N"));
try
{
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

    var saves = Path.Combine(installDir, "saves");
    Directory.CreateDirectory(Path.Combine(saves, "76561198000000001"));
    Directory.CreateDirectory(Path.Combine(saves, "76561198000000002"));
    Directory.CreateDirectory(Path.Combine(saves, "not-a-steamid"));
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
