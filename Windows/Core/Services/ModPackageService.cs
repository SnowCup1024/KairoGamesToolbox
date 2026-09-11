using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KairosoftGameToolbox.Services;

public sealed record ModFile(string Path, string Sha256);
public sealed record ModManifest(int SchemaVersion, uint AppId, string GameFolder, string Version,
    string Channel, string Description, List<ModFile> Targets, List<ModFile> Files);

/// <summary>声明式 ZIP 模组：先校验整个包和游戏，暂存后安装；更新只覆盖清单管理且指纹匹配的文件。</summary>
public static class ModPackageService
{
    private const long MaxBytes = 512L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public static string GameFolder(string englishName)
    {
        var name = Regex.Replace(englishName, "[^A-Za-z0-9]", "");
        if (name.Length == 0) throw new InvalidDataException(L.T("游戏缺少英文目录名。"));
        return name;
    }

    internal static string SafePath(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Contains('\\') || relative.Contains(':') ||
            relative.Split('/').Any(s => s.Length == 0 || s is "." or ".." || s.EndsWith('.') || s.EndsWith(' ') ||
                s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Regex.IsMatch(s, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)", RegexOptions.IgnoreCase)))
            throw new InvalidDataException(L.T("模组包含非法路径。"));
        var fullRoot = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!full.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.T("模组路径超出目标目录。"));
        for (var current = full; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(L.T("目标路径包含链接或重解析点，无法安全释放。"));
        return full;
    }

    private static bool AllowedPayload(string path) => path is "winhttp.dll" or "doorstop_config.ini" or ".doorstop_version" or "changelog.txt"
        || path.StartsWith("BepInEx/core/", StringComparison.Ordinal)
        || path.StartsWith("BepInEx/plugins/", StringComparison.Ordinal)
        || path.StartsWith("dotnet/", StringComparison.Ordinal)
        || path.StartsWith("licenses/", StringComparison.Ordinal);
    private static string Hash(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));
    private static bool Matches(string path, string hash)
    {
        using var stream = File.OpenRead(path);
        return Hash(stream).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }

    private static ModManifest Inspect(ZipArchive zip, uint appId, string folder)
    {
        if (zip.Entries.Count > 4096 || zip.Entries.Sum(e => e.Length) > MaxBytes)
            throw new InvalidDataException(L.T("模组超过文件数量或解压大小限制。"));
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in zip.Entries)
        {
            if (!entries.Add(e.FullName)) throw new InvalidDataException(L.T("ZIP 存在重复路径。"));
            SafePath(Path.GetTempPath(), e.FullName.TrimEnd('/'));
        }
        var manifestEntry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException(L.T("缺少 manifest.json；请选择工具箱格式的模组 ZIP。"));
        if (manifestEntry.Length > 1024 * 1024) throw new InvalidDataException(L.T("模组清单过大。"));
        using var json = manifestEntry.Open();
        var m = JsonSerializer.Deserialize<ModManifest>(json, JsonOptions) ?? throw new InvalidDataException(L.T("模组清单无效。"));
        if (m.SchemaVersion != 1 || m.AppId != appId || m.GameFolder != folder)
            throw new InvalidDataException(L.T("模组与当前选中的游戏不匹配。"));
        if (m.Channel is not ("Alpha" or "Beta" or "Stable") || !Regex.IsMatch(m.Version ?? "", @"^[0-9]+\.[0-9]+\.[0-9]+$"))
            throw new InvalidDataException(L.T("模组版本或发布阶段无效。"));
        if (m.Targets == null || m.Files == null || m.Files.Count == 0 || m.Targets.Count != 2 ||
            !m.Targets.Select(t => t.Path).ToHashSet().SetEquals(new[] { "GameAssembly.dll", "KairoGames_Data/il2cpp_data/Metadata/global-metadata.dat" }))
            throw new InvalidDataException(L.T("缺少游戏代码与元数据指纹。"));
        var payloadNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in m.Targets.Concat(m.Files))
        {
            SafePath(Path.GetTempPath(), f.Path);
            if (!Regex.IsMatch(f.Sha256 ?? "", "^[A-Fa-f0-9]{64}$")) throw new InvalidDataException(L.T("文件指纹格式无效。"));
        }
        foreach (var f in m.Files)
        {
            if (!AllowedPayload(f.Path) || !payloadNames.Add("payload/" + f.Path))
                throw new InvalidDataException(L.T("模组包含不允许释放的文件或重复文件。"));
            var e = zip.GetEntry("payload/" + f.Path) ?? throw new InvalidDataException(L.T("模组文件缺失。"));
            using var stream = e.Open();
            if (!Hash(stream).Equals(f.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L.T("模组文件校验失败：") + f.Path);
        }
        if (zip.Entries.Any(e => !e.FullName.EndsWith('/') && e.FullName != "manifest.json" && !payloadNames.Contains(e.FullName)))
            throw new InvalidDataException(L.T("ZIP 含未声明文件。"));
        return m;
    }

    public static ModManifest Read(string package, uint appId, string folder)
    {
        using var zip = ZipFile.OpenRead(package);
        return Inspect(zip, appId, folder);
    }

    public static string Import(string package, string modsRoot, uint appId, string folder)
    {
        Directory.CreateDirectory(modsRoot);
        var temporary = SafePath(modsRoot, ".import-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            if (new FileInfo(package).Length > MaxBytes) throw new InvalidDataException(L.T("ZIP 过大。"));
            File.Copy(package, temporary);
            var m = Read(temporary, appId, folder);
            Directory.CreateDirectory(SafePath(modsRoot, folder + "/Alpha"));
            Directory.CreateDirectory(SafePath(modsRoot, folder + "/Beta"));
            var relative = folder + "/" + (m.Channel == "Stable" ? "" : m.Channel + "/") + $"{folder}-{m.Version}.zip";
            var destination = SafePath(modsRoot, relative);
            if (File.Exists(destination))
            {
                using var source = File.OpenRead(temporary);
                if (!Matches(destination, Hash(source))) throw new IOException(L.T("同版本模组包已存在且内容不同，请先移走旧包。"));
            }
            else File.Move(temporary, destination);
            return destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private const string ReceiptName = ".kairomods-install.json";
    public static void Uninstall(string gameRoot, uint appId, string folder)
    {
        var previous = Previous(gameRoot, appId, folder) ?? throw new IOException(L.T("无法识别已安装的模组。"));
        var paths = previous.Files.Select(f => (File: f, Path: SafePath(gameRoot, f.Path))).ToList();
        foreach (var p in paths)
            if (File.Exists(p.Path) && !Matches(p.Path, p.File.Sha256))
                throw new IOException(L.T("模组文件已被修改，未删除任何文件：") + p.File.Path);
        string stage = SafePath(gameRoot, ".kairomods-uninstall-" + Guid.NewGuid().ToString("N"));
        var moved = new List<(string Source, string Backup)>();
        bool preserve = false;
        try
        {
            foreach (var relative in previous.Files.Select(f => f.Path).Append(ReceiptName))
            {
                string source = SafePath(gameRoot, relative);
                if (!File.Exists(source)) continue;
                var declared = previous.Files.SingleOrDefault(f => f.Path == relative);
                if (declared != null && !Matches(source, declared.Sha256)) throw new IOException(L.T("卸载期间文件发生变化。"));
                string backup = SafePath(stage, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Move(source, backup);
                moved.Add((source, backup));
            }
        }
        catch
        {
            try { foreach (var p in moved.AsEnumerable().Reverse()) File.Move(p.Backup, p.Source); }
            catch (Exception ex) { preserve = true; throw new IOException(L.T("卸载恢复未完成，备份保留在：") + stage, ex); }
            throw;
        }
        finally { if (!preserve && Directory.Exists(stage)) Directory.Delete(stage, true); }
        // Runtime-generated logs/config/interop and all unknown files are deliberately preserved.
        // Removing the managed loader and plugin restores unmodified game startup.
    }
    public static bool HasInstalledMod(string root) => File.Exists(Path.Combine(root, ReceiptName))
        || File.Exists(Path.Combine(root, "BepInEx/plugins/KairoMods.Observer/KairoMods.Observer.dll"));

    private static ModManifest? Previous(string root, uint appId, string folder)
    {
        var receipt = SafePath(root, ReceiptName);
        ModManifest? previous;
        if (File.Exists(receipt))
        {
            if (new FileInfo(receipt).Length > 1024 * 1024) throw new InvalidDataException(L.T("安装记录过大。"));
            previous = JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(receipt), JsonOptions);
        }
        else if (HasInstalledMod(root) && appId == 2934180)
        {
            using var resource = typeof(ModPackageService).Assembly.GetManifestResourceStream("KairosoftGameToolbox.LegacyModManifest.json")!;
            previous = JsonSerializer.Deserialize<ModManifest>(resource, JsonOptions);
            // 已手动验证的本地 0.0.4，仅允许这个确切 DLL 作为旧版识别。
            const string plugin = "BepInEx/plugins/KairoMods.Observer/KairoMods.Observer.dll";
            const string testedHash = "D8A673A7783FEF1822D738E15D47CC4EBE47D0ACDBC02D03BE4B4201849C1E19";
            if (File.Exists(SafePath(root, plugin)) && Matches(SafePath(root, plugin), testedHash))
                previous = previous! with { Files = previous!.Files.Select(f => f.Path == plugin ? new ModFile(plugin, testedHash) : f).ToList() };
        }
        else return null;
        if (previous == null || previous.SchemaVersion != 1 || previous.AppId != appId || previous.GameFolder != folder
            || previous.Files == null || previous.Files.Count == 0 || previous.Files.Count > 4096)
            throw new InvalidDataException(L.T("安装记录与当前游戏不匹配。"));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in previous.Files)
        {
            SafePath(root, f.Path);
            if (!AllowedPayload(f.Path) || !names.Add(f.Path) || !Regex.IsMatch(f.Sha256 ?? "", "^[A-Fa-f0-9]{64}$"))
                throw new InvalidDataException(L.T("旧安装记录包含无效文件。"));
        }
        return previous;
    }

    public static ModManifest Install(string package, string gameRoot, uint appId, string folder, bool update = false)
    {
        using var zip = ZipFile.OpenRead(package);
        var m = Inspect(zip, appId, folder);
        if (!GameFolderService.ContainsExecutable(gameRoot)) throw new IOException(L.T("目标目录缺少 KairoGames.exe。"));
        SafePath(gameRoot, "KairoGames.exe");
        foreach (var f in m.Targets)
        {
            var target = SafePath(gameRoot, f.Path);
            if (!File.Exists(target) || !Matches(target, f.Sha256))
                throw new InvalidDataException(L.T("游戏或版本不匹配，未释放任何文件：") + f.Path);
        }
        var old = update ? Previous(gameRoot, appId, folder) : null;
        if (update && old == null) throw new IOException(L.T("无法识别旧模组，未覆盖任何文件。"));
        var oldFiles = old?.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase) ?? new();
        var newFiles = m.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var paths = oldFiles.Keys.Union(newFiles.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        var originalHashes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in paths)
        {
            var target = SafePath(gameRoot, relative);
            if (Directory.Exists(target)) throw new IOException(L.T("目标文件位置存在目录：") + relative);
            string? hash = null;
            if (File.Exists(target)) { using var stream = File.OpenRead(target); hash = Hash(stream); }
            originalHashes[relative] = hash;
            if (hash == null) continue;
            bool knownOld = oldFiles.TryGetValue(relative, out var prior) && hash.Equals(prior.Sha256, StringComparison.OrdinalIgnoreCase);
            bool sameNew = newFiles.TryGetValue(relative, out var next) && hash.Equals(next.Sha256, StringComparison.OrdinalIgnoreCase);
            if (!knownOld && !sameNew) throw new IOException(L.T("未知或被修改的模组文件，拒绝覆盖：") + relative);
        }
        var receipt = SafePath(gameRoot, ReceiptName);
        var stage = SafePath(gameRoot, ".kairomods-" + Guid.NewGuid().ToString("N"));
        var changed = new List<(string Target, string? Backup)>();
        bool preserveStage = false;
        try
        {
            foreach (var f in m.Files)
            {
                var staged = SafePath(stage, "new/" + f.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                zip.GetEntry("payload/" + f.Path)!.ExtractToFile(staged);
            }
            // 先备份所有将被替换的文件，再开始提交写入。
            foreach (var relative in paths.Append(ReceiptName))
            {
                var target = SafePath(gameRoot, relative);
                if (!File.Exists(target)) continue;
                var backup = SafePath(stage, "backup/" + relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(target, backup);
            }
            foreach (var relative in paths)
            {
                var target = SafePath(gameRoot, relative);
                var before = originalHashes[relative];
                if (before == null ? File.Exists(target) : !File.Exists(target) || !Matches(target, before))
                    throw new IOException(L.T("安装期间文件发生变化。"));
                if (newFiles.TryGetValue(relative, out var next) && before != null && before.Equals(next.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (next != null) File.Move(SafePath(stage, "new/" + relative), target, true);
                else if (File.Exists(target)) File.Delete(target);
                changed.Add((target, before == null ? null : SafePath(stage, "backup/" + relative)));
            }
            var stagedReceipt = SafePath(stage, "receipt.json");
            File.WriteAllText(stagedReceipt, JsonSerializer.Serialize(m));
            var receiptBackup = File.Exists(receipt) ? SafePath(stage, "backup/" + ReceiptName) : null;
            File.Move(stagedReceipt, receipt, true);
            changed.Add((receipt, receiptBackup));
        }
        catch
        {
            try
            {
                var failures = new List<Exception>();
                foreach (var entry in changed.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (entry.Backup != null) File.Copy(entry.Backup, entry.Target, true);
                        else if (File.Exists(entry.Target)) File.Delete(entry.Target);
                    }
                    catch (Exception ex) { failures.Add(ex); }
                }
                if (failures.Count > 0) throw new AggregateException(failures);
            }
            catch (Exception rollback)
            {
                preserveStage = true;
                throw new IOException(L.T("自动恢复未完成，备份保留在：") + stage, rollback);
            }
            throw;
        }
        finally { if (!preserveStage && Directory.Exists(stage)) Directory.Delete(stage, true); }
        return m;
    }
}
