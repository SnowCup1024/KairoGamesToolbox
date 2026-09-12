using System.Diagnostics;
using System.Reflection;
using KairosoftGameToolbox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KairosoftGameToolbox.Views;

/// <summary>
/// 设置页：主题 / Steam 路径（覆盖）/ API Key / 游玩记录。
/// API Key 和 Steam 路径通过各自的保存按钮写入；主题即时保存到 %LocalAppData%\KairoGamesToolbox\settings.json。
/// </summary>
public sealed partial class SettingsPage : UserControl
{
    /// <summary>设置变化（Steam 路径 / API Key）→ 主窗触发游戏库重扫。</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>主题变化 → 主窗切换 RequestedTheme。</summary>
    public event EventHandler<string>? ThemeChanged;
    public event EventHandler? LanguageChanged;

    private bool _loading;
    private string? _apiKeyValue;
    private bool _apiKeyDirty;
    private bool _apiKeyMasked;

    public SettingsPage()
    {
        InitializeComponent();
        ApiKeyBox.AddHandler(UIElement.PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(ApiKeyBox_PointerPressed), true);
        PageMotion.Constrain(PageScroll, PageBody);
        PageBody.SizeChanged += (_, _) => ApiButtons.Orientation = PageBody.ActualWidth < 500 ? Orientation.Vertical : Orientation.Horizontal;
        Loaded += (_, _) => LoadFromSettings();
        Unloaded += (_, _) => RestoreApiKey();
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            if (Visibility != Visibility.Visible) RestoreApiKey();
        });
    }

    private void LoadFromSettings()
    {
        _loading = true;
        try
        {
            var s = SettingsService.Instance.Current;
            LanguageCombo.SelectedIndex = Array.IndexOf(L.Languages, L.Language);
            SteamPathBox.Text = s.SteamPathOverride ?? new SteamLibraryService().DetectSteamPath(null) ?? "";
            _apiKeyValue = s.SteamWebApiKey;
            _apiKeyDirty = false;
            _apiKeyMasked = !string.IsNullOrWhiteSpace(_apiKeyValue);
            ApiKeyBox.Text = "";
            ApiKeyBox.PlaceholderText = _apiKeyMasked ? MaskApiKey(_apiKeyValue!) : L.T("手动输入 API Key");
            ThemeCombo.SelectedIndex = s.ThemePreference switch
            {
                "light" => 1,
                "dark" => 2,
                _ => 0
            };
            VersionText.Text = L.T("开罗游戏工具箱") + " " + ReleaseInfo.DisplayVersion + " · GPL-3.0";
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task SaveAsync(bool saveSteamPath, bool saveApiKey = false, bool maskApiKey = false)
    {
        var s = SettingsService.Instance.Current;
        if (saveSteamPath)
        {
            var path = string.IsNullOrWhiteSpace(SteamPathBox.Text)
                ? new SteamLibraryService().DetectSteamPath(null)
                : SteamLibraryService.NormalizeDirectoryPath(SteamPathBox.Text);
            if (!SteamLibraryService.IsValidSteamPath(path))
            {
                await new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    RequestedTheme = ActualTheme,
                    Title = L.T("Steam 路径无效"),
                    Content = L.T("请选择同时包含 Steam.exe 和 steamapps 文件夹的 Steam 安装目录。当前设置未更改。"),
                    CloseButtonText = L.T("关闭"),
                }.ShowAsync();
                return;
            }
            s.SteamPathOverride = path;
            SteamPathBox.Text = path ?? "";
        }
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

    private async void SaveAndRescan_Click(object sender, RoutedEventArgs e)
    {
        await SaveAsync(saveSteamPath: true);
    }

    private async void SaveApiKeyAndRescan_Click(object sender, RoutedEventArgs e)
    {
        await SaveAsync(saveSteamPath: false, saveApiKey: true, maskApiKey: true);
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

    private void ApiKeyBox_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => ApiKeyBox_GotFocus(sender, e);

    private void ApiKeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_loading || !_apiKeyMasked) return;
        _loading = true;
        ApiKeyBox.Text = "";
        ApiKeyBox.PlaceholderText = L.T("手动输入 API Key");
        ApiKeyBox.SelectAll();
        _loading = false;
        _apiKeyMasked = false;
        _apiKeyDirty = true;
    }

    private void ApiKeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        // 焦点先于 Click 离开文本框；允许保存按钮读取草稿，其余离开操作丢弃未保存输入。
        DispatcherQueue.TryEnqueue(() =>
        {
            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot);
            if (IsWithin(focused as DependencyObject, SaveApiKeyButton) || IsWithin(focused as DependencyObject, ApiKeyBox))
                return;
            RestoreApiKey();
        });
    }

    private static bool IsWithin(DependencyObject? child, DependencyObject ancestor)
    {
        while (child != null)
        {
            if (ReferenceEquals(child, ancestor)) return true;
            child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(child);
        }
        return false;
    }

    private void RestoreApiKey()
    {
        _apiKeyValue = SettingsService.Instance.Current.SteamWebApiKey;
        _apiKeyDirty = false;
        SetApiKeyMasked();
    }

    private void ApiKeyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading && !_apiKeyMasked)
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

    private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageCombo.SelectedIndex < 0) return;
        SettingsService.Instance.Current.Language = L.Languages[LanguageCombo.SelectedIndex];
        if (SettingsService.Instance.Save()) LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetApiKeyMasked()
    {
        _loading = true;
        _apiKeyMasked = !string.IsNullOrWhiteSpace(_apiKeyValue);
        ApiKeyBox.Text = "";
            ApiKeyBox.PlaceholderText = _apiKeyMasked ? MaskApiKey(_apiKeyValue!) : L.T("手动输入 API Key");
        _loading = false;
    }

    private static string MaskApiKey(string apiKey)
        => new string('*', apiKey.Length);
}
