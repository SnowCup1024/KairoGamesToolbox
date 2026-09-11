using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KairosoftGameToolbox.Services;

public sealed class LauncherSettings
{
    public string? SteamPathOverride { get; set; }

    /// <summary>运行时明文，仅保留在内存中，不参与 JSON 序列化。</summary>
    [JsonIgnore]
    public string? SteamWebApiKey { get; set; }

    /// <summary>Windows DPAPI CurrentUser 密文（Base64）。</summary>
    public string? SteamWebApiKeyProtected { get; set; }

    public string ThemePreference { get; set; } = "system";
    public string Language { get; set; } = "zh-CN";
}

/// <summary>
/// 设置持久化：%LocalAppData%\KairoGamesToolbox\settings.json。
/// 任何改动即时落盘；API Key 以 DPAPI CurrentUser 密文写入 JSON，失败静默保持内存值。
/// </summary>
public sealed class SettingsService
{
    public static SettingsService Instance { get; } = new();

    private readonly string _file;

    public LauncherSettings Current { get; private set; } = new();

    public SettingsService() : this(null) { }

    /// <summary>允许无头测试使用临时配置路径；应用本身使用 LocalAppData 默认路径。</summary>
    public SettingsService(string? file)
    {
        if (file is null)
        {
            _file = AppDataPaths.SettingsFile;
        }
        else
        {
            _file = file;
        }
        Load();
    }

    /// <summary>在创建主窗口前初始化配置；测试可注入自动识别结果。</summary>
    public bool InitializeSteamPath(Func<string?>? detectSteamPath = null)
    {
        var previous = Current.SteamPathOverride;
        Current.SteamPathOverride = string.IsNullOrWhiteSpace(previous)
            ? (detectSteamPath ?? (() => new SteamLibraryService().DetectSteamPath(null)))()
            : SteamLibraryService.NormalizeDirectoryPath(previous);
        if (!string.IsNullOrWhiteSpace(Current.SteamPathOverride))
            Current.SteamPathOverride = SteamLibraryService.NormalizeDirectoryPath(Current.SteamPathOverride);
        return File.Exists(_file) && previous == Current.SteamPathOverride || Save();
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            Current = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(_file)) ?? new LauncherSettings();

            if (!string.IsNullOrWhiteSpace(Current.SteamWebApiKeyProtected))
            {
                Current.SteamWebApiKey = Dpapi.Unprotect(Current.SteamWebApiKeyProtected);
                // 密文损坏或不属于当前 Windows 用户时，按未配置处理，不能把密文当明文发送。
                if (Current.SteamWebApiKey == null)
                    Current.SteamWebApiKeyProtected = null;
            }
            if (!IsValidThemePreference(Current.ThemePreference))
                Current.ThemePreference = "system";
        }
        catch
        {
            Current = new LauncherSettings();
        }
    }

    public static bool IsValidThemePreference(string? preference)
        => preference is "system" or "light" or "dark";

    public bool Save()
    {
        try
        {
            var protectedKey = string.IsNullOrWhiteSpace(Current.SteamWebApiKey)
                ? null
                : Dpapi.Protect(Current.SteamWebApiKey);
            if (!string.IsNullOrWhiteSpace(Current.SteamWebApiKey) && protectedKey == null)
                return false;

            Current.SteamWebApiKeyProtected = protectedKey;
            var dir = Path.GetDirectoryName(_file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            var temp = _file + ".tmp";
            try
            {
                File.WriteAllText(temp, json);
                if (File.Exists(_file))
                    File.Replace(temp, _file, null);
                else
                    File.Move(temp, _file);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            return true;
        }
        catch
        {
            // 写盘失败不打断使用；临时文件只可能是启动器自己的配置临时文件。
            return false;
        }
    }

    private static class Dpapi
    {
        private const uint CryptProtectUiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(
            ref DataBlob dataIn,
            string? description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr prompt,
            uint flags,
            out DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(
            ref DataBlob dataIn,
            IntPtr description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr prompt,
            uint flags,
            out DataBlob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr handle);

        public static string? Protect(string value)
            => Transform(value, protect: true);

        public static string? Unprotect(string value)
        {
            try
            {
                var encrypted = Convert.FromBase64String(value);
                var plain = Transform(encrypted, protect: false);
                return plain == null ? null : Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return null;
            }
        }

        private static string? Transform(string value, bool protect)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            try
            {
                var transformed = Transform(bytes, protect);
                return transformed == null ? null : Convert.ToBase64String(transformed);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        private static byte[]? Transform(byte[] bytes, bool protect)
        {
            if (!OperatingSystem.IsWindows()) return null;

            IntPtr inputMemory = IntPtr.Zero;
            DataBlob output = default;
            try
            {
                inputMemory = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, inputMemory, bytes.Length);
                var input = new DataBlob { Size = bytes.Length, Data = inputMemory };
                bool ok = protect
                    ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        CryptProtectUiForbidden, out output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        CryptProtectUiForbidden, out output);
                if (!ok || output.Data == IntPtr.Zero || output.Size <= 0) return null;

                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (inputMemory != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(inputMemory);
                }
                if (output.Data != IntPtr.Zero)
                    LocalFree(output.Data);
            }
        }
    }
}
