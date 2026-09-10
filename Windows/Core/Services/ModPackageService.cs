using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KairosoftGameToolbox.Services;

public sealed record ModFile(string Path, string Sha256);
public sealed record ModManifest(int SchemaVersion, uint AppId, string GameFolder, string Version,
    string Channel, string Description, List<ModFile> Targets, List<ModFile> Files);

/// <summary>声明式 ZIP 模组：先校验整个包和游戏，暂存后安装；不同内容的已有文件拒绝覆盖。</summary>
public static class ModPackageService
{
    private const long MaxBytes = 512L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public static string GameFolder(string englishName)
    {
        var name = Regex.Replace(englishName, "[^A-Za-z0-9]", "");
        if (name.Length == 0) throw new InvalidDataException("游戏缺少英文目录名。");
        return name;
    }

    private static string SafePath(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Contains('\\') || relative.Contains(':') ||
            relative.Split('/').Any(s => s.Length == 0 || s is "." or ".." || s.EndsWith('.') || s.EndsWith(' ') ||
                s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Regex.IsMatch(s, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)", RegexOptions.IgnoreCase)))
            throw new InvalidDataException("模组包含非法路径。");
        var fullRoot = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!full.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("模组路径超出目标目录。");
        for (var current = full; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("目标路径包含链接或重解析点，无法安全释放。");
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
            throw new InvalidDataException("模组超过文件数量或解压大小限制。");
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in zip.Entries)
        {
            if (!entries.Add(e.FullName)) throw new InvalidDataException("ZIP 存在重复路径。");
            SafePath(Path.GetTempPath(), e.FullName.TrimEnd('/'));
        }
        var manifestEntry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("缺少 manifest.json；请选择工具箱格式的模组 ZIP。");
        if (manifestEntry.Length > 1024 * 1024) throw new InvalidDataException("模组清单过大。");
        using var json = manifestEntry.Open();
        var m = JsonSerializer.Deserialize<ModManifest>(json, JsonOptions) ?? throw new InvalidDataException("模组清单无效。");
        if (m.SchemaVersion != 1 || m.AppId != appId || m.GameFolder != folder)
            throw new InvalidDataException("模组与当前选中的游戏不匹配。");
        if (m.Channel is not ("Alpha" or "Beta" or "Stable") || !Regex.IsMatch(m.Version ?? "", @"^[0-9]+\.[0-9]+\.[0-9]+$"))
            throw new InvalidDataException("模组版本或发布阶段无效。");
        if (m.Targets == null || m.Files == null || m.Files.Count == 0 || m.Targets.Count != 2 ||
            !m.Targets.Select(t => t.Path).ToHashSet().SetEquals(new[] { "GameAssembly.dll", "KairoGames_Data/il2cpp_data/Metadata/global-metadata.dat" }))
            throw new InvalidDataException("缺少游戏代码与元数据指纹。");
        var payloadNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in m.Targets.Concat(m.Files))
        {
            SafePath(Path.GetTempPath(), f.Path);
            if (!Regex.IsMatch(f.Sha256 ?? "", "^[A-Fa-f0-9]{64}$")) throw new InvalidDataException("文件指纹格式无效。");
        }
        foreach (var f in m.Files)
        {
            if (!AllowedPayload(f.Path) || !payloadNames.Add("payload/" + f.Path))
                throw new InvalidDataException("模组包含不允许释放的文件或重复文件。");
            var e = zip.GetEntry("payload/" + f.Path) ?? throw new InvalidDataException("模组文件缺失。");
            using var stream = e.Open();
            if (!Hash(stream).Equals(f.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("模组文件校验失败：" + f.Path);
        }
        if (zip.Entries.Any(e => !e.FullName.EndsWith('/') && e.FullName != "manifest.json" && !payloadNames.Contains(e.FullName)))
            throw new InvalidDataException("ZIP 含未声明文件。");
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
            if (new FileInfo(package).Length > MaxBytes) throw new InvalidDataException("ZIP 过大。");
            File.Copy(package, temporary);
            var m = Read(temporary, appId, folder);
            Directory.CreateDirectory(SafePath(modsRoot, folder + "/Alpha"));
            Directory.CreateDirectory(SafePath(modsRoot, folder + "/Beta"));
            var relative = folder + "/" + (m.Channel == "Stable" ? "" : m.Channel + "/") + $"{folder}-{m.Version}.zip";
            var destination = SafePath(modsRoot, relative);
            if (File.Exists(destination))
            {
                using var source = File.OpenRead(temporary);
                if (!Matches(destination, Hash(source))) throw new IOException("同版本模组包已存在且内容不同，请先移走旧包。");
            }
            else File.Move(temporary, destination);
            return destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static ModManifest Install(string package, string gameRoot, uint appId, string folder)
    {
        using var zip = ZipFile.OpenRead(package);
        var m = Inspect(zip, appId, folder);
        if (!GameFolderService.ContainsExecutable(gameRoot)) throw new IOException("目标目录缺少 KairoGames.exe。");
        SafePath(gameRoot, "KairoGames.exe");
        foreach (var f in m.Targets)
        {
            var target = SafePath(gameRoot, f.Path);
            if (!File.Exists(target) || !Matches(target, f.Sha256))
                throw new InvalidDataException("游戏或版本不匹配，未释放任何文件：" + f.Path);
        }
        // 在所有写入之前完成冲突检查。
        foreach (var f in m.Files)
        {
            var target = SafePath(gameRoot, f.Path);
            if (Directory.Exists(target) || (File.Exists(target) && !Matches(target, f.Sha256)))
                throw new IOException("已有不同内容的文件，拒绝覆盖：" + f.Path);
        }
        var stage = SafePath(gameRoot, ".kairomods-" + Guid.NewGuid().ToString("N"));
        var created = new List<string>();
        try
        {
            foreach (var f in m.Files)
            {
                var file = SafePath(stage, f.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                zip.GetEntry("payload/" + f.Path)!.ExtractToFile(file);
            }
            foreach (var f in m.Files)
            {
                var target = SafePath(gameRoot, f.Path);
                if (File.Exists(target))
                {
                    if (!Matches(target, f.Sha256)) throw new IOException("安装期间目标文件发生变化。");
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(SafePath(stage, f.Path), target);
                created.Add(target);
            }
        }
        catch
        {
            foreach (var file in created.AsEnumerable().Reverse()) File.Delete(file);
            throw;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        return m;
    }
}
