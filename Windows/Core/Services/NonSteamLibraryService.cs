using System.Text.Json;
using System.Text;

namespace KairosoftGameToolbox.Services;

public sealed record NonSteamGame(uint AppId, string Directory);

/// <summary>只读取游戏身份，不运行 EXE。目录名不作为游戏身份依据。</summary>
public sealed class NonSteamLibraryService
{
    private readonly string file;
    public NonSteamLibraryService(string? file = null) => this.file = file ?? AppDataPaths.NonSteamLibraryFile;

    public List<NonSteamGame> Load()
    {
        if (!File.Exists(file)) return new();
        return JsonSerializer.Deserialize<List<NonSteamGame>>(File.ReadAllText(file))
            ?? throw new InvalidDataException(L.T("非 Steam 游戏库记录无效，请检查 non-steam-games.json。"));
    }

    public void Save(uint appId, string directory)
    {
        var entries = Load();
        entries.RemoveAll(e => e.AppId == appId || string.Equals(e.Directory, directory, StringComparison.OrdinalIgnoreCase));
        entries.Add(new(appId, SteamLibraryService.NormalizeDirectoryPath(directory)));
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, file, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Remove(uint appId)
    {
        var entries = Load();
        if (entries.RemoveAll(e => e.AppId == appId) == 0) return;
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries));
            File.Move(temporary, file, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static CatalogEntry Identify(string directory, AppIdCatalog catalog)
    {
        if (!GameFolderService.ContainsExecutable(directory)) throw new IOException(L.T("目录中未找到 KairoGames.exe。"));
        var info = Path.Combine(directory, "KairoGames_Data", "app.info");
        if (!File.Exists(info)) throw new InvalidDataException(L.T("缺少 KairoGames_Data/app.info，无法确认游戏身份。"));
        var lines = File.ReadAllLines(info);
        if (lines.Length < 2 || !lines[0].Trim().Equals("Kairosoft", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.T("所选目录不是可识别的开罗游戏。"));
        var title = lines[1].Trim().Normalize(NormalizationForm.FormKC);
        var aliases = new Dictionary<string, uint>
        {
            ["ドラえもんのどら焼き屋さん物語"] = 2934180,
            ["大盛グルメ食堂"] = 1952170,
            ["開店コンビニ日記"] = 2119650,
            ["開店デパート日記2"] = 1983700,
            ["名門ポケット学院3"] = 2191490,
        };
        var matches = catalog.Entries.Where(e => title.Equals(e.Name.Normalize(NormalizationForm.FormKC), StringComparison.OrdinalIgnoreCase)
            || title.Equals(e.ChineseName.Normalize(NormalizationForm.FormKC), StringComparison.OrdinalIgnoreCase)
            || (aliases.TryGetValue(title, out var id) && e.AppId == id)).ToList();
        var appIdFile = Path.Combine(directory, "steam_appid.txt");
        if (File.Exists(appIdFile) && uint.TryParse(File.ReadAllText(appIdFile).Trim(), out var declared))
        {
            if (matches.Count > 0 && matches.All(e => e.AppId != declared)) throw new InvalidDataException(L.T("游戏名称与 AppID 不一致。"));
            if (matches.Count == 0) matches = catalog.Entries.Where(e => e.AppId == declared).ToList();
        }
        if (matches.Count != 1) throw new InvalidDataException(L.F("暂未收录此游戏的身份标识：{0}。未添加；请提供 app.info 以补充识别。", title));
        return matches[0];
    }
}
