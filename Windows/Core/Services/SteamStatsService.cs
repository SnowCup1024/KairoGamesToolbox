using System.Text.Json;
using KairosoftGameToolbox.Models;

namespace KairosoftGameToolbox.Services;

/// <summary>
/// Steam 游玩记录（可选，默认关）：
/// Steam Web API IPlayerService/GetOwnedGames（官方，钥匙所有者本人返回完整时长）。
/// SteamID 由调用方通过 SteamAccountService 统一识别并传入。
/// 任何失败返回 null/空 → UI 静默隐藏时长信息，不阻塞主界面。
/// </summary>
public sealed class SteamStatsService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    // 会话内缓存，避免每次切页都打网络
    private Dictionary<uint, GamePlayStats>? _cache;
    private DateTime _cacheTime;
    private string? _cacheApiKey;
    private string? _cacheSteamId;

    /// <summary>拉取持有游戏的游玩时长（分钟）与最近游玩（Unix 秒）。失败返回 null。</summary>
    public async Task<Dictionary<uint, GamePlayStats>?> FetchOwnedGamesAsync(
        string apiKey, string steamId, CancellationToken cancellationToken = default)
    {
        if (_cache != null
            && string.Equals(_cacheApiKey, apiKey, StringComparison.Ordinal)
            && string.Equals(_cacheSteamId, steamId, StringComparison.Ordinal)
            && DateTime.UtcNow - _cacheTime < TimeSpan.FromMinutes(10))
            return _cache;

        try
        {
            var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/"
                    + $"?key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId)}"
                    + "&include_appinfo=true&include_played_free_games=true&format=json";
            using var resp = await Http.GetAsync(url, cancellationToken);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("response", out var response)
                || !response.TryGetProperty("games", out var games)
                || games.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var result = new Dictionary<uint, GamePlayStats>();
            foreach (var g in games.EnumerateArray())
            {
                if (!g.TryGetProperty("appid", out var a)) continue;
                uint appId = a.GetUInt32();
                long playtime = g.TryGetProperty("playtime_forever", out var pf) ? pf.GetInt64() : 0;
                long? twoWeeks = g.TryGetProperty("playtime_2weeks", out var p2) ? p2.GetInt64() : null;
                // Steam GetOwnedGames 使用 rtime_last_played。
                long? lastPlayed = ReadUnixTime(g, "rtime_last_played");
                result[appId] = new GamePlayStats(appId, playtime, twoWeeks, lastPlayed);
            }

            _cache = result;
            _cacheTime = DateTime.UtcNow;
            _cacheApiKey = apiKey;
            _cacheSteamId = steamId;
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static long? ReadUnixTime(JsonElement game, string propertyName)
    {
        if (!game.TryGetProperty(propertyName, out var value)) return null;
        long timestamp = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), out var text) => text,
            _ => 0,
        };
        return timestamp > 0 ? timestamp : null;
    }
}
