using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using KairoMods.Protocol;
using KairosoftGameToolbox.Services;

static class BetaChecks
{
    public static void Run(Action<string, bool> check)
    {
        check("本版对外 v1.0 Beta 3，内部 1.0.3", ReleaseInfo.DisplayVersion == "v1.0 Beta 3" && Version.Parse(ReleaseInfo.Version).Build > 0);
        check("正式版显示 Release 并隐藏内部构建序号", ReleaseInfo.FormatDisplay("2.3.5", false) == "v2.3 Release");
        check("测试版展示对应测试序号", ReleaseInfo.FormatDisplay("2.3.4", true) == "v2.3 Beta 4");
        bool rejectedZero = false;
        try { ReleaseInfo.FormatDisplay("2.3.0", true); } catch (ArgumentException) { rejectedZero = true; }
        check("版本序号不允许零", rejectedZero);
        var definition = BundledModService.ForGame(2934180)!;
        check("五项功能定义来自游戏模组", definition.Features.Select(f => f.Id).ToHashSet().SetEquals(new[] { "moneyReverse", "fPointReverse", "coinReverse", "trainingReverse", "itemReverse" }));
        var searchable = new KairosoftGameToolbox.Models.KairoGame { AppId = 2934180, EnglishName = "Doraemon Dorayaki Shop Story" };
        check("搜索同时匹配简体、繁体、英文和日文名称", GameSearch.Matches(searchable, "铜锣烧") && GameSearch.Matches(searchable, "銅鑼燒") && GameSearch.Matches(searchable, "Doraemon") && GameSearch.Matches(searchable, "どら焼き"));
        check("每项功能具有四语言名称与说明", definition.Features.All(f => L.Languages.All(l => !string.IsNullOrEmpty(f.Names.GetValueOrDefault(l)) && !string.IsNullOrEmpty(f.Descriptions.GetValueOrDefault(l)))));
        foreach (var file in definition.Files)
        {
            using var stream = typeof(L).Assembly.GetManifestResourceStream("ModPayload.2934180." + file.Path);
            check("内嵌游戏文件指纹匹配 " + file.Path, stream != null && Convert.ToHexString(SHA256.HashData(stream)) == file.Sha256);
        }
        using (var stream = typeof(L).Assembly.GetManifestResourceStream("Localization.json")!)
        {
            var texts = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)!;
            check("所有翻译资源完整覆盖繁体、英文和日文", texts.Count >= 90 && texts.Values.All(v => new[] { "zh-TW", "en", "ja" }.All(l => !string.IsNullOrWhiteSpace(v.GetValueOrDefault(l)))));
        }
        using (var stream = typeof(L).Assembly.GetManifestResourceStream("GameNames.json")!)
        {
            using var document = JsonDocument.Parse(stream);
            check("63款游戏均有四种语言名称", document.RootElement.GetProperty("games").EnumerateArray().All(g => new[] { "name", "name_zh_cn", "name_zh_tw", "name_ja" }.All(k => !string.IsNullOrWhiteSpace(g.GetProperty(k).GetString()))));
        }
        string root = Path.Combine(Path.GetTempPath(), "KairoBeta-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            check("无安装记录不判断为最新", !ModPackageService.IsCurrent(root, definition));
            var files = new List<ModFile>();
            foreach (var file in definition.Files)
            {
                string destination = Path.Combine(root, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var input = typeof(L).Assembly.GetManifestResourceStream("ModPayload.2934180." + file.Path)!;
                using (var output = File.Create(destination)) input.CopyTo(output);
                files.Add(file);
            }
            foreach (var name in new[] { "winhttp.dll", "BepInEx/core/BepInEx.Unity.IL2CPP.dll" })
            {
                string destination = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllText(destination, "synthetic runtime");
                files.Add(new(name, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination)))));
            }
            var receipt = new ModManifest(1, definition.AppId, definition.GameFolder, definition.Version, "Beta", "test", definition.Targets, files);
            string receiptPath = Path.Combine(root, ".kairomods-install.json");
            File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt));
            check("当前版本且所有管理文件完整时为最新", ModPackageService.IsCurrent(root, definition));
            File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt with { Version = "1.0.1" }));
            check("旧版安装记录允许更新", !ModPackageService.IsCurrent(root, definition));
            File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt));
            File.AppendAllText(Path.Combine(root, definition.Files[0].Path), "modified");
            check("篡改专用 DLL 后不能声称最新完整", !ModPackageService.IsCurrent(root, definition));
            check("跨游戏安装记录不能声称最新", !ModPackageService.IsCurrent(root, definition with { AppId = 1 }));
            File.WriteAllText(receiptPath, "invalid json");
            check("损坏安装记录不导致状态检测崩溃", !ModPackageService.IsCurrent(root, definition));
            var library = new NonSteamLibraryService(Path.Combine(root, "library.json")); library.Save(1, root); library.Remove(1);
            check("移除非 Steam 只删除库记录，目录仍存在", library.Load().Count == 0 && Directory.Exists(root));
            var states = definition.Features.ToDictionary(f => f.Id, _ => new FeatureState()); states["moneyReverse"] = new(true, 5);
            ModSessionCache.Set(2934180, root, states); states["moneyReverse"] = new(false, 1);
            check("状态缓存保留副本且不同目录隔离", ModSessionCache.Get(2934180, root)!["moneyReverse"] == new FeatureState(true, 5) && ModSessionCache.Get(2934180, root + "other") == null);
            ModSessionCache.Clear(2934180, root);
            check("删除模组可清除会话记忆", ModSessionCache.Get(2934180, root) == null);
            string path = Path.Combine(root, "LogOutput.log");
            File.WriteAllText(path, "line1\n", new UTF8Encoding(false));
            using var reader = new ModLogReader(path);
            check("日志初次读取且重复读取不重复显示", reader.Read().SequenceEqual(new[] { "line1" }) && reader.Read().Count == 0);
            File.AppendAllText(path, "半行", new UTF8Encoding(false));
            check("未完成日志行等待换行", reader.Read().Count == 0);
            File.AppendAllText(path, "结束\n", new UTF8Encoding(false));
            check("日志合并未完成中文行", reader.Read().Single() == "半行结束");
            File.WriteAllText(path, "new\n", new UTF8Encoding(false));
            check("日志截断后重新读取", reader.Read().Single() == "new");
            string line = "[Info] ResourceResult | time=2026-09-12T01:00:00+08:00 | id=1 | resource=moneyReverse | before=100 | after=150 | delta=50";
            check("资源日志按游戏规则只翻译最终增量", ModLogReader.Format(line, definition, "zh-CN").Text.EndsWith("增加金钱 50"));
            check("开发模式保留原始日志", ModLogReader.Format(line, definition with { Development = true }, "zh-CN").Text == line);
            string duplicate = line + " | delta=99";
            check("重复字段日志保留原文且不导致崩溃", ModLogReader.Format(duplicate, definition, "zh-CN").Text == duplicate);
            check("资源错误日志不会被成功模板掩盖", ModLogReader.Format("[Error] " + line, definition, "en").Error);
            check("未识别日志与错误不丢弃", ModLogReader.Format("[Error] fault", definition, "en").Error);
        }
        finally { Directory.Delete(root, true); }
    }
}
