using System.Diagnostics;
using System.Reflection;
using KairosoftGameToolbox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KairosoftGameToolbox.Views;

/// <summary>
/// 设置页：主题 / Steam 路径（覆盖）/ API Key / 游玩记录。
/// API Key 和 Steam 路径通过各自的保存按钮写入；主题即时保存到 %LocalAppData%\KairosoftGameToolbox\settings.json。
/// </summary>
public sealed partial class SettingsPage : UserControl
{
    /// <summary>设置变化（Steam 路径 / API Key）→ 主窗触发游戏库重扫。</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>主题变化 → 主窗切换 RequestedTheme。</summary>
    public event EventHandler<string>? ThemeChanged;

    private bool _loading;
    private string? _apiKeyValue;
    private bool _apiKeyDirty;
    private bool _apiKeyMasked;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        _loading = true;
        try
        {
            var s = SettingsService.Instance.Current;
            SteamPathBox.Text = s.SteamPathOverride ?? new SteamLibraryService().DetectSteamPath(null) ?? "";
            _apiKeyValue = s.SteamWebApiKey;
            _apiKeyDirty = false;
            _apiKeyMasked = !string.IsNullOrWhiteSpace(_apiKeyValue);
            ApiKeyBox.Text = _apiKeyMasked ? MaskApiKey(_apiKeyValue!) : "";
            ThemeCombo.SelectedIndex = s.ThemePreference switch
            {
                "light" => 1,
                "dark" => 2,
                _ => 0
            };
            VersionText.Text = $"开罗游戏工具箱 v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)} · GPL-3.0";
        }
        finally
        {
            _loading = false;
        }
    }

    private void Save(bool saveSteamPath, bool saveApiKey = false, bool maskApiKey = false)
    {
        var s = SettingsService.Instance.Current;
        if (saveSteamPath)
            s.SteamPathOverride = string.IsNullOrWhiteSpace(SteamPathBox.Text) ? null : SteamPathBox.Text.Trim();
        if (saveApiKey)
        {
            _apiKeyValue = _apiKeyDirty && !string.IsNullOrWhiteSpace(ApiKeyBox.Text)
                ? ApiKeyBox.Text.Trim()
                : _apiKeyDirty ? null : _apiKeyValue;
            s.SteamWebApiKey = _apiKeyValue;
            _apiKeyDirty = false;
        }
        SettingsService.Instance.Save();
        if (maskApiKey) SetApiKeyMasked();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SaveAndRescan_Click(object sender, RoutedEventArgs e)
    {
        Save(saveSteamPath: true);
    }

    private void SaveApiKeyAndRescan_Click(object sender, RoutedEventArgs e)
    {
        Save(saveSteamPath: false, saveApiKey: true, maskApiKey: true);
    }

    private void OpenApiKeyPage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://steamcommunity.com/dev/apikey",
                UseShellExecute = true,
            });
        }
        catch
        {
            // 浏览器启动失败不影响启动器使用
        }
    }

    private void ApiKeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_loading || !_apiKeyMasked) return;
        _loading = true;
        ApiKeyBox.Text = _apiKeyValue ?? "";
        ApiKeyBox.SelectAll();
        _loading = false;
        _apiKeyMasked = false;
    }

    private void ApiKeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading || _apiKeyDirty || string.IsNullOrWhiteSpace(_apiKeyValue)) return;
        SetApiKeyMasked();
    }

    private void ApiKeyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading)
        {
            _apiKeyDirty = true;
            _apiKeyMasked = false;
        }
    }

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeCombo.SelectedIndex < 0) return;
        var preference = (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "system";
        var s = SettingsService.Instance.Current;
        s.ThemePreference = SettingsService.IsValidThemePreference(preference) ? preference : "system";
        SettingsService.Instance.Save();
        ThemeChanged?.Invoke(this, s.ThemePreference);
    }

    private void SetApiKeyMasked()
    {
        if (string.IsNullOrWhiteSpace(_apiKeyValue))
        {
            _apiKeyMasked = false;
            return;
        }

        _loading = true;
        ApiKeyBox.Text = MaskApiKey(_apiKeyValue);
        _loading = false;
        _apiKeyMasked = true;
    }

    private static string MaskApiKey(string apiKey)
        => new string('*', apiKey.Length);
}
