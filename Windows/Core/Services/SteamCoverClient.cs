using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KairosoftGameToolbox.Services;

/// <summary>从 Steam 商店元数据解析 library JPG 地址，按磁盘缓存 → 网络 → Steam 本地缓存的顺序加载。</summary>
public sealed class SteamCoverClient
{
    private sealed record CacheEntry(DateTime ExpiresAt, Lazy<Task<byte[]?>> Download);

    private readonly HttpClient _http;
    private readonly string? _cacheDirectory;
    private readonly Func<IEnumerable<string>>? _steamPaths;
    private readonly Func<byte[], Task<bool>>? _validateImage;
    private const int MaximumImageBytes = 8 * 1024 * 1024;
    private readonly SemaphoreSlim _downloads = new(4, 4);
    private readonly ConcurrentDictionary<uint, CacheEntry> _cache = new();

    public SteamCoverClient(HttpClient http, string? cacheDirectory = null,
        Func<IEnumerable<string>>? steamPaths = null, Func<byte[], Task<bool>>? validateImage = null)
    {
        _http = http;
        _cacheDirectory = cacheDirectory;
        _steamPaths = steamPaths;
        _validateImage = validateImage;
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
        try
        {
            var cachePath = _cacheDirectory == null ? null : Path.Combine(_cacheDirectory, $"{appId}.jpg");
            if (cachePath != null)
            {
                result = await ReadImageAsync(cachePath).ConfigureAwait(false);
                if (result != null) return result;
            }

            await _downloads.WaitAsync().ConfigureAwait(false);
            try
            {
                result = await FetchImageAsync(appId).ConfigureAwait(false);
            }
            finally
            {
                _downloads.Release();
            }

            if (result == null && _steamPaths != null)
            {
                foreach (var steamPath in _steamPaths().Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    result = await ReadImageAsync(Path.Combine(steamPath, "appcache", "librarycache",
                        appId.ToString(), "library_600x900.jpg")).ConfigureAwait(false);
                    if (result != null) break;
                }
            }
            if (result != null && cachePath != null)
                await SaveImageAsync(cachePath, result).ConfigureAwait(false);
            return result;
        }
        finally
        {
            // 成功保留一小时；失败短暂缓存，避免卡片重建时反复请求，随后允许恢复。
            if (_cache.TryGetValue(appId, out var entry))
                _cache.TryUpdate(appId, entry with
                {
                    ExpiresAt = DateTime.UtcNow.Add(result == null ? TimeSpan.FromSeconds(30) : TimeSpan.FromHours(1)),
                }, entry);
        }
    }

    private async Task<byte[]?> FetchImageAsync(uint appId)
    {
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
            return await IsValidImageAsync(bytes).ConfigureAwait(false) ? bytes : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return null;
        }
    }

    private async Task<bool> IsValidImageAsync(byte[] bytes)
        => bytes.Length is >= 3 and <= MaximumImageBytes
            && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF
            && (_validateImage == null || await _validateImage(bytes).ConfigureAwait(false));

    private async Task<byte[]?> ReadImageAsync(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaximumImageBytes) return null;
            var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            return await IsValidImageAsync(bytes).ConfigureAwait(false) ? bytes : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static async Task SaveImageAsync(string path, byte[] bytes)
    {
        // 独立临时文件避免多进程互相覆盖半写入文件；失败不影响当前已取得的封面。
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(temporary, bytes).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // 只保留当前进程的内存结果，下次启动可以重新尝试写入。
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
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
