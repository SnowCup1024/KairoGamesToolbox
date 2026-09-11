using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace KairosoftGameToolbox.Services;

public sealed record RuntimePackage(string Id, string Url, string Sha256);
public sealed record GameModDefinition(uint AppId, string GameFolder, string Version, bool Development,
    RuntimePackage Runtime, List<ModFile> Targets, List<ModFile> Files, List<GameModFeature> Features,
    Dictionary<string, string> LogTemplates);
public sealed record GameModFeature(string Id, Dictionary<string, string> Names, Dictionary<string, string> Descriptions,
    Dictionary<string, string>? LogNames = null);

public static class BundledModService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly SemaphoreSlim gate = new(1, 1);
    public static IReadOnlyList<GameModDefinition> Definitions { get; } = Load();
    private static List<GameModDefinition> Load()
    {
        var assembly = typeof(BundledModService).Assembly;
        return assembly.GetManifestResourceNames().Where(n => n.StartsWith("Mods.") && n.EndsWith("definition.json"))
            .Select(n => { using var input = assembly.GetManifestResourceStream(n)!; return JsonSerializer.Deserialize<GameModDefinition>(input, JsonOptions)!; }).ToList();
    }
    public static GameModDefinition? ForGame(uint appId) => Definitions.SingleOrDefault(d => d.AppId == appId);
    public static string Text(Dictionary<string, string> values, string language) => values.GetValueOrDefault(language) ?? values.GetValueOrDefault("en") ?? "";

    public static async Task InstallAsync(uint appId, string target, CancellationToken token = default)
    {
        var definition = ForGame(appId) ?? throw new InvalidOperationException(L.T("此游戏暂未提供模组。"));
        // Verify game before any download, and again inside the transactional installer.
        foreach (var f in definition.Targets)
        {
            string path = ModPackageService.SafePath(target, f.Path);
            if (!File.Exists(path) || !Hash(path).Equals(f.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L.T("游戏或版本与模组不匹配：") + f.Path);
        }
        await gate.WaitAsync(token);
        string? composed = null;
        try
        {
            var runtime = definition.Runtime;
            string cache = ModPackageService.SafePath(AppDataPaths.ModsDirectory, "Runtime/" + runtime.Id + ".zip");
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            if (!File.Exists(cache) || !Hash(cache).Equals(runtime.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                string partial = cache + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    var uri = new Uri(runtime.Url);
                    if (uri.Scheme != "https" || uri.Host != "builds.bepinex.dev") throw new InvalidDataException("Invalid runtime source");
                    using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                    using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();
                    await using (var source = await response.Content.ReadAsStreamAsync(token))
                    await using (var output = File.Create(partial))
                    {
                        var buffer = new byte[81920]; long length = 0; int read;
                        while ((read = await source.ReadAsync(buffer, token)) != 0)
                        {
                            length += read;
                            if (length > 128L * 1024 * 1024) throw new InvalidDataException("Runtime download too large");
                            await output.WriteAsync(buffer.AsMemory(0, read), token);
                        }
                    }
                    if (!Hash(partial).Equals(runtime.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L.T("运行组件校验失败。"));
                    File.Move(partial, cache, true);
                }
                finally { if (File.Exists(partial)) File.Delete(partial); }
            }
            composed = ModPackageService.SafePath(AppDataPaths.ModsDirectory, ".install-" + Guid.NewGuid().ToString("N") + ".zip");
            await Task.Run(() => Compose(composed, cache, definition), token);
            token.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                var running = System.Diagnostics.Process.GetProcessesByName("KairoGames");
                try { if (running.Length > 0) throw new IOException(L.T("请先退出正在运行的开罗游戏。")); }
                finally { foreach (var process in running) process.Dispose(); }
                ModPackageService.Install(composed, target, appId, definition.GameFolder, update: ModPackageService.HasInstalledMod(target));
            }, token);
        }
        finally { if (composed != null && File.Exists(composed)) File.Delete(composed); gate.Release(); }
    }
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    internal static void Compose(string path, string runtime, GameModDefinition definition)
    {
        using var output = ZipFile.Open(path, ZipArchiveMode.Create);
        using var loader = ZipFile.OpenRead(runtime);
        var files = new List<ModFile>();
        void Add(string name, Stream input, string? expected = null)
        {
            using var memory = new MemoryStream(); input.CopyTo(memory);
            string hash = Convert.ToHexString(SHA256.HashData(memory.ToArray()));
            if (expected != null && !hash.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Bundled mod hash mismatch");
            using var destination = output.CreateEntry("payload/" + name).Open(); memory.Position = 0; memory.CopyTo(destination);
            files.Add(new(name, hash));
        }
        foreach (var entry in loader.Entries.Where(e => !e.FullName.EndsWith('/')))
        {
            using var input = entry.Open(); Add(entry.FullName, input);
        }
        foreach (var f in definition.Files)
        {
            using var input = typeof(BundledModService).Assembly.GetManifestResourceStream("ModPayload." + definition.AppId + "." + f.Path)
                ?? throw new InvalidDataException("Bundled mod payload missing");
            Add(f.Path, input, f.Sha256);
        }
        foreach (var name in typeof(BundledModService).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("RuntimeNotice.")))
        {
            using var input = typeof(BundledModService).Assembly.GetManifestResourceStream(name)!;
            Add("licenses/KairoMods/" + name["RuntimeNotice.".Length..], input);
        }
        using var json = output.CreateEntry("manifest.json").Open();
        JsonSerializer.Serialize(json, new ModManifest(1, definition.AppId, definition.GameFolder, definition.Version,
            "Beta", "Bundled game mod", definition.Targets, files));
    }
}
