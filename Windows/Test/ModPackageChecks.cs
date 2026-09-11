using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KairosoftGameToolbox.Services;

static class ModPackageChecks
{
    public static void Run(Action<string, bool> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "KairoModTest-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        string Target(string name)
        {
            var dir = Path.Combine(root, name);
            Directory.CreateDirectory(Path.Combine(dir, "KairoGames_Data/il2cpp_data/Metadata"));
            File.WriteAllText(Path.Combine(dir, "KairoGames.exe"), "exe");
            File.WriteAllText(Path.Combine(dir, "GameAssembly.dll"), "code");
            File.WriteAllText(Path.Combine(dir, "KairoGames_Data/il2cpp_data/Metadata/global-metadata.dat"), "metadata");
            return dir;
        }
        var targets = new List<ModFile> { new("GameAssembly.dll", Hash("code")), new("KairoGames_Data/il2cpp_data/Metadata/global-metadata.dat", Hash("metadata")) };
        string Package(string name, string channel = "Alpha", string path = "BepInEx/plugins/test.dll", string content = "plugin", string? extra = null, string declaredContent = "plugin")
        {
            var file = Path.Combine(root, name + ".zip");
            using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
            void Write(string entry, string text) { using var writer = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false)); writer.Write(text); }
            Write("manifest.json", JsonSerializer.Serialize(new ModManifest(1, 123, "TestGame", "0.0.1", channel, "Test", targets, new() { new(path, Hash(declaredContent)) })));
            Write("payload/" + path, content);
            if (extra != null) Write(extra, "extra");
            return file;
        }
        void Reject(string name, Action action)
        {
            try { action(); check(name, false); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or JsonException or UnauthorizedAccessException) { check(name, true); }
        }
        try
        {
            var package = Package("valid");
            var dir = Target("game");
            var mods = Path.Combine(root, "Mods");
            var imported = ModPackageService.Import(package, mods, 123, "TestGame");
            check("Alpha 模组独立归档，并创建 Beta 目录", File.Exists(imported) && imported.Contains("Alpha") && Directory.Exists(Path.Combine(mods, "TestGame/Beta")));
            ModPackageService.Install(imported, dir, 123, "TestGame");
            check("匹配目标释放模组，游戏原文件保持不变", File.ReadAllText(Path.Combine(dir, "BepInEx/plugins/test.dll")) == "plugin" && File.ReadAllText(Path.Combine(dir, "GameAssembly.dll")) == "code");
            ModPackageService.Install(imported, dir, 123, "TestGame");
            check("相同模组可以重复安装", true);
            check("安装后可检测模组", ModPackageService.HasInstalledMod(dir));
            var removable = Target("remove");
            ModPackageService.Install(imported, removable, 123, "TestGame");
            File.WriteAllText(Path.Combine(removable, "BepInEx/plugins/other.dll"), "other mod");
            File.WriteAllText(Path.Combine(removable, "save.dat"), "save");
            using (var locked = new FileStream(Path.Combine(removable, ".kairomods-install.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Reject("卸载记录提交失败会回退", () => ModPackageService.Uninstall(removable, 123, "TestGame"));
                check("卸载失败恢复插件", File.Exists(Path.Combine(removable, "BepInEx/plugins/test.dll")));
            }
            ModPackageService.Uninstall(removable, 123, "TestGame");
            check("卸载删除受管理文件与记录", !File.Exists(Path.Combine(removable, "BepInEx/plugins/test.dll")) && !ModPackageService.HasInstalledMod(removable));
            check("卸载保留原游戏、存档和未知插件", File.ReadAllText(Path.Combine(removable, "GameAssembly.dll")) == "code" && File.ReadAllText(Path.Combine(removable, "save.dat")) == "save" && File.Exists(Path.Combine(removable, "BepInEx/plugins/other.dll")));
            var update = Package("update", path: "BepInEx/plugins/new.dll");
            ModPackageService.Install(update, dir, 123, "TestGame", update: true);
            check("更新清理旧清单文件并安装新文件", !File.Exists(Path.Combine(dir, "BepInEx/plugins/test.dll")) && File.Exists(Path.Combine(dir, "BepInEx/plugins/new.dll")));
            ModPackageService.Install(imported, dir, 123, "TestGame", update: true);
            var legacy = Target("unknown-legacy");
            Directory.CreateDirectory(Path.Combine(legacy, "BepInEx/plugins"));
            File.WriteAllText(Path.Combine(legacy, "BepInEx/plugins/test.dll"), "unknown");
            Reject("未知旧模组不能强制覆盖", () => ModPackageService.Install(imported, legacy, 123, "TestGame", update: true));
            var newer = Package("newer", content: "plugin-v2", declaredContent: "plugin-v2");
            ModPackageService.Install(newer, dir, 123, "TestGame", update: true);
            check("更新直接替换已知旧 DLL", File.ReadAllText(Path.Combine(dir, "BepInEx/plugins/test.dll")) == "plugin-v2");
            ModPackageService.Install(imported, dir, 123, "TestGame", update: true);
            using (var lockedReceipt = new FileStream(Path.Combine(dir, ".kairomods-install.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Reject("更新提交记录失败会回退", () => ModPackageService.Install(newer, dir, 123, "TestGame", update: true));
                check("更新失败恢复被替换的旧 DLL", File.ReadAllText(Path.Combine(dir, "BepInEx/plugins/test.dll")) == "plugin");
            }
            var rollbackDir = Target("rollback");
            Directory.CreateDirectory(Path.Combine(rollbackDir, ".kairomods-install.json"));
            Reject("提交安装记录失败会报告错误", () => ModPackageService.Install(imported, rollbackDir, 123, "TestGame"));
            check("安装失败撤销新写入文件", !File.Exists(Path.Combine(rollbackDir, "BepInEx/plugins/test.dll")));
            Reject("模组 AppID 错误被拒绝", () => ModPackageService.Read(package, 456, "TestGame"));
            Reject("模组游戏目录标识错误被拒绝", () => ModPackageService.Read(package, 123, "Other"));
            var wrong = Target("wrong");
            File.WriteAllText(Path.Combine(wrong, "GameAssembly.dll"), "other version");
            Reject("游戏版本指纹错误被拒绝", () => ModPackageService.Install(package, wrong, 123, "TestGame"));
            check("指纹不匹配时不写入模组", !Directory.Exists(Path.Combine(wrong, "BepInEx")));
            File.WriteAllText(Path.Combine(dir, "BepInEx/plugins/test.dll"), "existing");
            Reject("已有不同模组文件拒绝覆盖", () => ModPackageService.Install(package, dir, 123, "TestGame"));
            Reject("更新也拒绝被篡改的已安装文件", () => ModPackageService.Install(package, dir, 123, "TestGame", update: true));
            check("冲突文件内容保留", File.ReadAllText(Path.Combine(dir, "BepInEx/plugins/test.dll")) == "existing");
            Reject("卸载拒绝删除被修改的文件", () => ModPackageService.Uninstall(dir, 123, "TestGame"));
            check("卸载预检查失败保留文件和记录", File.ReadAllText(Path.Combine(dir, "BepInEx/plugins/test.dll")) == "existing" && ModPackageService.HasInstalledMod(dir));
            Reject("拒绝 ZIP 路径越界", () => ModPackageService.Read(Package("traversal", extra: "../escape"), 123, "TestGame"));
            Reject("拒绝大小写重复 ZIP 条目", () => ModPackageService.Read(Package("duplicate", extra: "payload/BepInEx/plugins/TEST.dll"), 123, "TestGame"));
            Reject("拒绝修改游戏原文件的模组包", () => ModPackageService.Read(Package("forbidden", path: "GameAssembly.dll"), 123, "TestGame"));
            Reject("拒绝损坏的模组文件", () => ModPackageService.Read(Package("corrupt", content: "corrupt"), 123, "TestGame"));
            Reject("拒绝未声明的文件", () => ModPackageService.Read(Package("extra", extra: "payload/winhttp.dll"), 123, "TestGame"));
            Reject("拒绝非法发布阶段", () => ModPackageService.Read(Package("channel", channel: "../"), 123, "TestGame"));
            var stable = ModPackageService.Import(Package("stable", "Stable"), mods, 123, "TestGame");
            check("Stable 直接归档在游戏目录", Path.GetDirectoryName(stable) == Path.Combine(mods, "TestGame"));
            check("英文游戏目录移除空格标点", ModPackageService.GameFolder("Doraemon Dorayaki Shop Story") == "DoraemonDorayakiShopStory");
        }
        finally { Directory.Delete(root, true); }
    }
}
