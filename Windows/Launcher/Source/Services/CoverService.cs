using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace KairosoftGameToolbox.Services;

/// <summary>
/// 从随程序发布的 Data/Assets/Covers 目录只读加载封面。
/// 封面不再读取 Steam 缓存、不访问 CDN，也不写入用户目录；内存缓存只用于复用已加载的图片对象。
/// </summary>
public sealed class CoverService
{
    public static CoverService Default { get; } = new();

    private readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> _imageCache = new();

    private CoverService() { }

    /// <summary>清空内存图片缓存；下一次加载会重新读取发布目录中的封面文件。</summary>
    public void Reload()
        => _imageCache.Clear();

    /// <summary>加载 Data/Assets/Covers/&lt;appid&gt;.jpg；文件缺失或无法解码时返回 null。</summary>
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
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "Assets", "Covers", $"{appId}.jpg");
        if (!File.Exists(path)) return null;

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            if (saturation >= 0.999)
            {
                var bmp = new BitmapImage();
                await bmp.SetSourceAsync(stream);
                return bmp;
            }

            var decoder = await BitmapDecoder.CreateAsync(stream);
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
