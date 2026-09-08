using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KairosoftGameToolbox.Services;

/// <summary>从 Steam 商店元数据解析 library JPG 地址，下载结果只缓存在内存中。</summary>
public sealed class SteamCoverClient
{
    private sealed record CacheEntry(DateTime ExpiresAt, Lazy<Task<byte[]?>> Download);

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _downloads = new(4, 4);
    private readonly ConcurrentDictionary<uint, CacheEntry> _cache = new();

    public SteamCoverClient(HttpClient http)
    {
        _http = http;
    }

    public Task<byte[]?> DownloadAsync(uint appId)
    {
        var entry = _cache.AddOrUpdate(appId,
            id => CreateEntry(id),
            (id, previous) => previous.ExpiresAt > DateTime.UtcNow ? previous : CreateEntry(id));
        return entry.Download.Value;
    }

    private CacheEntry CreateEntry(uint appId)
        => new(DateTime.MaxValue, new Lazy<Task<byte[]?>>(() => DownloadCoreAsync(appId)));

    private async Task<byte[]?> DownloadCoreAsync(uint appId)
    {
        byte[]? result = null;
        await _downloads.WaitAsync().ConfigureAwait(false);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var input = JsonSerializer.Serialize(new
            {
                ids = new[] { new { appid = appId } },
                context = new { language = "english", country_code = "US" },
                data_request = new { include_assets = true },
            });
            var endpoint = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json="
                + Uri.EscapeDataString(input);
            var json = await _http.GetStringAsync(endpoint, timeout.Token).ConfigureAwait(false);
            var uri = ParseCoverUri(json, appId);
            if (uri == null) return null;

            var bytes = await _http.GetByteArrayAsync(uri, timeout.Token).ConfigureAwait(false);
            // 解码与尺寸检查由界面层完成；拒绝 HTML 错误页或其他非 JPEG 响应。
            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                result = bytes;
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return null;
        }
        finally
        {
            _downloads.Release();
            // 成功保留一小时；失败短暂缓存，避免卡片重建时反复请求，随后允许恢复。
            if (_cache.TryGetValue(appId, out var entry))
                _cache.TryUpdate(appId, entry with
                {
                    ExpiresAt = DateTime.UtcNow.Add(result == null ? TimeSpan.FromSeconds(30) : TimeSpan.FromHours(1)),
                }, entry);
        }
    }

    /// <summary>2x 字段对应 600×900；普通 library_capsule 实际可能只有 300×450。</summary>
    public static Uri? ParseCoverUri(string json, uint appId)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("response", out var response)
                || !response.TryGetProperty("store_items", out var items)
                || items.ValueKind != JsonValueKind.Array) return null;

            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("appid", out var id) || !id.TryGetUInt32(out var value) || value != appId
                    || !item.TryGetProperty("success", out var success) || !success.TryGetInt32(out var status) || status != 1
                    || !item.TryGetProperty("assets", out var assets)
                    || !assets.TryGetProperty("asset_url_format", out var formatElement)
                    || formatElement.ValueKind != JsonValueKind.String
                    || !assets.TryGetProperty("library_capsule_2x", out var filenameElement)
                    || filenameElement.ValueKind != JsonValueKind.String) continue;

                var format = formatElement.GetString()!;
                var filename = filenameElement.GetString()!;
                // 限定为对应 AppID 的 Steam CDN 资源，不接受任意外部地址或路径穿越。
                if (!Regex.IsMatch(format, @"^steam/apps/" + appId + @"/\$\{FILENAME\}(?:\?t=\d+)?$")
                    || !Regex.IsMatch(filename, @"^(?:[a-fA-F0-9]{40}/)?[a-zA-Z0-9_]+\.jpg$")) continue;

                return new Uri("https://shared.akamai.steamstatic.com/store_item_assets/"
                    + format.Replace("${FILENAME}", filename, StringComparison.Ordinal));
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
        return null;
    }
}
