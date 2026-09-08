using System.Text.Json;

namespace KairosoftGameToolbox.Services;

public sealed record CatalogEntry(uint AppId, string Name, string ChineseName)
{
    public string FullName => $"{ChineseName} ({Name})";
}

/// <summary>
/// 读取 Launcher/Data/KairosoftGames.json（63 款开罗游戏 Steam AppID 表）。
/// 判定开罗游戏时以文件检测为主、本表兜底；英文名用于内部标识，简体中文名用于界面显示。
/// </summary>
public sealed class AppIdCatalog
{
    private readonly Dictionary<uint, CatalogEntry> _entries = new();

    public IReadOnlyCollection<CatalogEntry> Entries { get; }

    public int Count => _entries.Count;

    public AppIdCatalog(string jsonPath)
    {
        var list = new List<CatalogEntry>();
        if (File.Exists(jsonPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                if (doc.RootElement.TryGetProperty("games", out var games))
                {
                    foreach (var g in games.EnumerateArray())
                    {
                        if (!g.TryGetProperty("appid", out var a) || !g.TryGetProperty("name", out var n)) continue;
                        uint appId = a.GetUInt32();
                        string name = n.GetString() ?? "";
                        string chineseName = g.TryGetProperty("name_zh_cn", out var zh)
                            ? zh.GetString() ?? ""
                            : "";
                        if (string.IsNullOrWhiteSpace(chineseName)) chineseName = name;

                        var entry = new CatalogEntry(appId, name, chineseName);
                        list.Add(entry);
                        _entries[appId] = entry;
                    }
                }
            }
            catch
            {
                // 表缺失/损坏时静默降级：只靠文件检测
            }
        }
        Entries = list;
    }

    public bool Contains(uint appId) => _entries.ContainsKey(appId);

    /// <summary>返回稳定的英文名，供内部排序、日志和兼容调用使用。</summary>
    public string? TryGetName(uint appId)
        => _entries.TryGetValue(appId, out var entry) ? entry.Name : null;

    public string? TryGetChineseName(uint appId)
        => _entries.TryGetValue(appId, out var entry) ? entry.ChineseName : null;
}
