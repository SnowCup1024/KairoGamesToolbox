using System.Text.Json;

namespace KairosoftGameToolbox.Services;

public static class L
{
    public static readonly string[] Languages = { "zh-CN", "zh-TW", "en", "ja" };
    public static string Language => Languages.Contains(SettingsService.Instance.Current.Language) ? SettingsService.Instance.Current.Language : "zh-CN";
    private static readonly Dictionary<string, Dictionary<string, string>> translations = Load();
    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        using var source = typeof(L).Assembly.GetManifestResourceStream("Localization.json");
        return source == null ? new() : JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(source)!;
    }
    public static string T(string source) => Language == "zh-CN" ? source
        : translations.TryGetValue(source, out var texts) ? texts.GetValueOrDefault(Language) ?? source : source;
    public static string F(string source, params object?[] args) => string.Format(T(source), args);
    private static readonly Dictionary<uint, Dictionary<string, string>> gameNames = LoadGames();
    private static Dictionary<uint, Dictionary<string, string>> LoadGames()
    {
        using var source = typeof(L).Assembly.GetManifestResourceStream("GameNames.json");
        if (source == null) return new();
        using var json = JsonDocument.Parse(source);
        return json.RootElement.GetProperty("games").EnumerateArray().ToDictionary(g => g.GetProperty("appid").GetUInt32(),
            g => new Dictionary<string, string> { ["zh-CN"] = g.GetProperty("name_zh_cn").GetString()!, ["zh-TW"] = g.GetProperty("name_zh_tw").GetString()!,
                ["en"] = g.GetProperty("name").GetString()!, ["ja"] = g.GetProperty("name_ja").GetString()! });
    }
    public static string GameName(uint appId, string fallback) => gameNames.TryGetValue(appId, out var names) ? names[Language] : fallback;
    public static IEnumerable<string> GameSearchNames(uint appId) => gameNames.TryGetValue(appId, out var names) ? names.Values : Array.Empty<string>();
}
