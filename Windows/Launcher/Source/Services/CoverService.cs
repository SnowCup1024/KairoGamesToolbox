using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace KairosoftGameToolbox.Services;

/// <summary>
/// 从 Steam CDN 加载 600×900 library JPG，资源地址由商店元数据提供。
/// 优先读取用户 Covers 缓存，联网失败后尝试 Steam 本地封面；均不可用时显示占位图。
/// </summary>
public sealed class CoverService
{
    public static CoverService Default { get; } = new();

    private readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> _imageCache = new();

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
        MaxResponseContentBufferSize = 8 * 1024 * 1024,
    };
    private static readonly SteamCoverClient Downloads = new(Http, AppDataPaths.CoversDirectory,
        FindSteamPaths, ValidateImageAsync);

    private static IEnumerable<string> FindSteamPaths()
    {
        var detected = new SteamLibraryService().DetectSteamPath(SettingsService.Instance.Current.SteamPathOverride);
        if (detected != null) yield return detected;
        yield return @"E:\Steam";
    }

    private static bool HasCoverDimensions(uint width, uint height)
        => (width == 600 && height == 900) || (width == 300 && height == 450);

    private static async Task<bool> ValidateImageAsync(byte[] bytes)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (!HasCoverDimensions(decoder.PixelWidth, decoder.PixelHeight)) return false;
            // 完整解码后才写缓存，避免只有 JPEG 文件头的损坏文件持续阻止下载。
            _ = await decoder.GetPixelDataAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private CoverService() { }

    /// <summary>同一游戏和饱和度复用图片；下载失败时允许后续重新加载。</summary>
    public async Task<ImageSource?> LoadCoverImageAsync(uint appId, double saturation = 1.0)
    {
        saturation = Math.Clamp(saturation, 0, 1);
        var key = $"{appId}|{saturation:0.##}";
        var lazy = _imageCache.GetOrAdd(
            key,
            _ => new Lazy<Task<ImageSource?>>(() => LoadCoverImageCoreAsync(appId, saturation)));
        var result = await lazy.Value;
        if (result == null)
        {
            ((ICollection<KeyValuePair<string, Lazy<Task<ImageSource?>>>>)_imageCache)
                .Remove(new KeyValuePair<string, Lazy<Task<ImageSource?>>>(key, lazy));
        }
        return result;
    }

    private static async Task<ImageSource?> LoadCoverImageCoreAsync(uint appId, double saturation)
    {
        try
        {
            var bytes = await Downloads.DownloadAsync(appId);
            if (bytes == null) return null;
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
            }
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (!HasCoverDimensions(decoder.PixelWidth, decoder.PixelHeight)) return null;
            stream.Seek(0);
            if (saturation >= 0.999)
            {
                var bmp = new BitmapImage();
                await bmp.SetSourceAsync(stream);
                return bmp;
            }

            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage);
            var pixels = pixelData.DetachPixelData();
            var width = checked((int)decoder.OrientedPixelWidth);
            var height = checked((int)decoder.OrientedPixelHeight);
            var adjusted = await Task.Run(() => ApplySaturation(pixels, saturation));

            var writable = new WriteableBitmap(width, height);
            using (var output = writable.PixelBuffer.AsStream())
            {
                output.Write(adjusted, 0, adjusted.Length);
            }
            writable.Invalidate();
            return writable;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] ApplySaturation(byte[] pixels, double saturation)
    {
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            byte gray = (byte)Math.Clamp((int)Math.Round(r * 0.2126 + g * 0.7152 + b * 0.0722), 0, 255);
            pixels[i] = Blend(gray, b, saturation);
            pixels[i + 1] = Blend(gray, g, saturation);
            pixels[i + 2] = Blend(gray, r, saturation);
        }
        return pixels;
    }

    private static byte Blend(byte gray, byte color, double saturation)
        => (byte)Math.Clamp((int)Math.Round(gray + (color - gray) * saturation), 0, 255);
}
