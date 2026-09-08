using System.Diagnostics;
using KairosoftGameToolbox.Controls;
using KairosoftGameToolbox.Models;
using KairosoftGameToolbox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KairosoftGameToolbox.Views;

/// <summary>
/// 游戏库页：识别 → 展示（封面网格）→ 启动入口。
/// 数据流：SteamLibraryService 扫描（文件检测 + KairosoftGames.json 兜底）→ 双语 KairoGame 列表 → 卡片。
/// </summary>
public sealed partial class LibraryPage : UserControl
{
    private readonly AppIdCatalog _catalog;
    private readonly SteamLibraryService _steam = new();
    private readonly SteamStatsService _statsService = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _refreshSync = new();

    private List<KairoGame> _allGames = new();
    private string _search = "";
    private CancellationTokenSource? _refreshCancellation;

    public LibraryPage()
    {
        InitializeComponent();
        _catalog = new AppIdCatalog(Path.Combine(AppContext.BaseDirectory, "Data", "KairosoftGames.json"));
        Loaded += async (_, _) => await RefreshAsync();
    }

    /// <summary>重新扫描游戏库并重建视图；新请求会取消旧请求，扫描本身串行执行。</summary>
    public async Task RefreshAsync()
    {
        var cancellation = BeginRefresh();
        try
        {
            await _refreshGate.WaitAsync(cancellation.Token);
            try
            {
                var steamOverride = SettingsService.Instance.Current.SteamPathOverride;
                var games = await Task.Run(() =>
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    _steam.Scan(steamOverride, cancellation.Token);
                    return BuildGameList();
                }, cancellation.Token);

                cancellation.Token.ThrowIfCancellationRequested();
                _allGames = games;
                RebuildView();

                await ApplySteamDataIfAvailableAsync(cancellation.Token);
            }
            finally
            {
                _refreshGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // 新刷新请求取消当前操作后，不再提交其结果。
        }
        finally
        {
            lock (_refreshSync)
            {
                if (ReferenceEquals(_refreshCancellation, cancellation))
                {
                    _refreshCancellation = null;
                    BusyRing.IsActive = false;
                }
            }
            cancellation.Dispose();
        }
    }

    private CancellationTokenSource BeginRefresh()
    {
        lock (_refreshSync)
        {
            _refreshCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _refreshCancellation = cancellation;
            BusyRing.IsActive = true;
            return cancellation;
        }
    }

    /// <summary>目录中的全部条目 + 文件检测到但不在表中的已装开罗游戏。</summary>
    private List<KairoGame> BuildGameList()
    {
        var installed = _steam.KairoInstalledApps(_catalog);
        var preferredSteamId = FindMostRecentSteamId(_steam.SteamPath);
        var list = new List<KairoGame>();

        foreach (var entry in _catalog.Entries)
        {
            installed.TryGetValue(entry.AppId, out var info);
            list.Add(MakeGame(entry.AppId, entry.ChineseName, entry.Name, info, preferredSteamId));
        }

        // 文件检测到但不在 AppID 表 → 以安装目录名兜底展示
        foreach (var (appId, info) in installed)
        {
            if (_catalog.Contains(appId)) continue;
            var englishName = Path.GetFileName(info.InstallDir);
            list.Add(MakeGame(appId, englishName, englishName, info, preferredSteamId));
        }

        list.Sort((a, b) => string.Compare(a.SortName, b.SortName, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    private KairoGame MakeGame(
        uint appId,
        string displayName,
        string englishName,
        InstalledAppInfo? info,
        string? preferredSteamId)
    {
        var game = new KairoGame
        {
            AppId = appId,
            Name = displayName,
            EnglishName = englishName,
            IsInstalled = info != null,
        };
        if (info != null)
        {
            game.InstallDir = info.InstallDir;
            game.LibraryPath = info.LibraryPath;
            game.LastPlayedUnix = info.LastPlayedUnix;
            game.SaveDir = SaveDirectoryService.Find(info.InstallDir, preferredSteamId);
        }
        return game;
    }

    private static string? FindMostRecentSteamId(string? steamPath)
    {
        if (string.IsNullOrWhiteSpace(steamPath)) return null;
        var loginFile = Path.Combine(steamPath, "config", "loginusers.vdf");
        if (!File.Exists(loginFile)) return null;

        try
        {
            var vdf = VdfParser.Parse(File.ReadAllText(loginFile));
            var users = vdf.TryGetValue("users", out var usersValue)
                && usersValue is Dictionary<string, object> nestedUsers
                ? nestedUsers
                : vdf;
            string? bestId = null;
            int best = -1;
            foreach (var (key, value) in users)
            {
                if (value is not Dictionary<string, object> account || !ulong.TryParse(key, out _)) continue;
                if (account.TryGetValue("mostrecent", out var mostRecent)
                    && int.TryParse(mostRecent as string, out var flag)
                    && flag > best)
                {
                    best = flag;
                    bestId = key;
                }
            }
            return bestId;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>加载可选 Steam 数据：拥有状态用于排序，配置 API Key 后显示游玩记录。</summary>
    private async Task ApplySteamDataIfAvailableAsync(CancellationToken cancellationToken)
    {
        var s = SettingsService.Instance.Current;
        foreach (var game in _allGames)
        {
            game.Ownership = OwnershipStatus.Unknown;
            game.Stats = null;
        }

        if (string.IsNullOrWhiteSpace(s.SteamWebApiKey)) return;
        var steamId = await Task.Run(() => _statsService.ResolveSteamId(_steam), cancellationToken);
        if (steamId == null) return;

        cancellationToken.ThrowIfCancellationRequested();
        var stats = await _statsService.FetchOwnedGamesAsync(
            s.SteamWebApiKey, steamId, cancellationToken);
        if (stats == null) return;
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var g in _allGames)
        {
            if (stats.TryGetValue(g.AppId, out var st))
            {
                g.Ownership = OwnershipStatus.Owned;
                g.Stats = st;
                if (st.LastPlayedUnix is { } lastPlayed && lastPlayed > 0)
                    g.LastPlayedUnix = lastPlayed;
            }
            else
            {
                g.Ownership = OwnershipStatus.NotOwned;
            }
        }
        RebuildView();
    }

    private void RebuildView()
    {
        var filtered = _allGames
            .Where(g => GameSearch.Matches(g, _search))
            .ToList();
        var sections = GameOrdering.GroupAndOrder(filtered)
            .ToDictionary(section => section.Kind);

        SetSection(
            InstalledSection,
            InstalledSectionTitle,
            InstalledRepeater,
            sections.GetValueOrDefault(GameSectionKind.Installed));
        SetSection(
            NotInstalledSection,
            NotInstalledSectionTitle,
            NotInstalledRepeater,
            sections.GetValueOrDefault(GameSectionKind.NotInstalled));
        SetSection(
            NotOwnedSection,
            NotOwnedSectionTitle,
            NotOwnedRepeater,
            sections.GetValueOrDefault(GameSectionKind.NotOwnedOrUnknown));

        bool empty = filtered.Count == 0;
        GridScroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = _search.Length > 0 ? "没有匹配的游戏" : "未检测到开罗游戏";
        EmptyDescription.Text = _search.Length > 0
            ? "请尝试其他搜索关键词"
            : "请确认 Steam 已安装并登录";
    }

    private static void SetSection(
        FrameworkElement sectionRoot,
        TextBlock sectionTitle,
        ItemsRepeater repeater,
        GameSection? section)
    {
        bool visible = section != null;
        sectionRoot.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        sectionTitle.Text = visible ? $"{section!.Title} · {section.Games.Count}款" : "";
        repeater.ItemsSource = section?.Games ?? Array.Empty<KairoGame>();
    }

    // ---- 交互 ----

    private async void Card_GameClicked(object sender, KairoGame game)
    {
        await ShowLaunchDialogAsync(game);
    }

    private async void Card_LaunchRequested(object sender, KairoGame game)
    {
        if (game.IsInstalled)
            await LaunchGameAsync(game);
        else
            await ShowLaunchDialogAsync(game);
    }

    private async void Card_SaveDirectoryRequested(object sender, KairoGame game)
    {
        var saveDir = game.SaveDir;
        if (!game.IsInstalled || string.IsNullOrWhiteSpace(saveDir)) return;

        if (!Directory.Exists(saveDir))
        {
            await ShowSaveDirectoryErrorAsync("未找到该游戏当前 SteamID 的存档目录。");
            return;
        }

        try
        {
            // UseShellExecute 交给 Windows Shell 打开目录；返回 null 也可能表示
            // Explorer 已接管请求，不代表打开失败。真正失败以异常为准。
            Process.Start(new ProcessStartInfo
                {
                    FileName = saveDir,
                    UseShellExecute = true,
                });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"打开存档目录失败: {ex.Message}");
            await ShowSaveDirectoryErrorAsync("系统无法打开该文件夹，请检查文件管理器设置或路径是否可用。");
        }
    }

    private async Task ShowSaveDirectoryErrorAsync(string message)
    {
        var errorDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = "打开存档目录失败",
            Content = message,
            CloseButtonText = "关闭",
        };
        await errorDialog.ShowAsync();
    }

    private async Task ShowLaunchDialogAsync(KairoGame game)
    {
        if (!game.IsInstalled)
        {
            bool owned = game.Ownership == OwnershipStatus.Owned;
            var installContent = new StackPanel { Spacing = 12, MinWidth = 280 };
            installContent.Children.Add(await CreateCoverPreviewAsync(game));
            installContent.Children.Add(new TextBlock
            {
                Text = $"{game.LibraryStatusText} · AppID {game.AppId}",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            });

            var installDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = game.Name,
                Content = installContent,
                PrimaryButtonText = owned
                    ? "去安装"
                    : game.Ownership == OwnershipStatus.NotOwned ? "去购买" : "查看商店",
                CloseButtonText = "关闭",
                DefaultButton = ContentDialogButton.Primary,
            };
            var installResult = await installDialog.ShowAsync();
            if (installResult == ContentDialogResult.Primary)
            {
                if (owned)
                    await OpenSteamUriAsync(SteamLinkService.Install(game.AppId), "打开 Steam 安装入口");
                else
                    await OpenExternalAsync(
                        SteamLinkService.Store(game.AppId),
                        "打开 Steam 商店页面",
                        "请确认系统默认浏览器可用。");
            }
            return;
        }

        var thumb = await CoverService.Default.LoadCoverImageAsync(
            game.AppId,
            game.CoverSaturation);

        var coverBox = new Border
        {
            Width = 150,
            Height = 210,
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["CoverGradientBrush"],
            Child = new Grid(),
        };
        var coverGrid = (Grid)coverBox.Child;
        if (thumb != null)
        {
            coverGrid.Children.Add(new Image { Source = thumb, Stretch = Stretch.UniformToFill });
        }
        else
        {
            coverGrid.Children.Add(new TextBlock
            {
                Text = game.Name.Length > 0 ? game.Name[..1].ToUpperInvariant() : "?",
                FontSize = 48,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var content = new StackPanel { Spacing = 12, MinWidth = 280 };
        content.Children.Add(coverBox);
        content.Children.Add(new TextBlock
        {
            Text = game.IsInstalled ? $"已安装 · AppID {game.AppId}" : $"未安装 · AppID {game.AppId}",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
        });
        content.Children.Add(new TextBlock
        {
            Text = "Mod 补丁将直接修改游戏原文件。当前版本尚未提供补丁，请使用“仅启动”。",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
        });

        var patchButton = new Button
        {
            Content = "应用 Mod 补丁",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            // ContentDialog 的默认按钮边框会产生约 1px 的视觉内缩，向两侧扩展后与下方按钮组等宽。
            Margin = new Thickness(-1, 0, -1, 0),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 212)),
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
        };
        ToolTipService.SetToolTip(patchButton, "查看应用 Mod 补丁的注意事项");

        var actionGrid = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 4, 0, 0) };
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var launchButton = new Button
        {
            Content = "仅启动",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8),
        };
        var cancelButton = new Button
        {
            Content = "取消",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8),
        };
        Grid.SetColumn(launchButton, 0);
        Grid.SetColumn(cancelButton, 1);
        actionGrid.Children.Add(launchButton);
        actionGrid.Children.Add(cancelButton);
        content.Children.Add(patchButton);
        content.Children.Add(actionGrid);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = game.Name,
            Content = content,
        };
        bool patchRequested = false;
        bool launchRequested = false;
        patchButton.Click += (_, _) =>
        {
            patchRequested = true;
            dialog.Hide();
        };
        launchButton.Click += (_, _) =>
        {
            launchRequested = true;
            dialog.Hide();
        };
        cancelButton.Click += (_, _) => dialog.Hide();
        _ = await dialog.ShowAsync();
        // 等待第一个弹窗完全关闭后再显示二级弹窗，避免 WinUI 同时打开多个 ContentDialog。
        if (patchRequested)
            await ShowPatchWarningAsync(game);
        else if (launchRequested)
            await LaunchGameAsync(game);
    }

    private async Task ShowPatchWarningAsync(KairoGame game)
    {
        var warning = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = "应用 Mod 补丁 · " + game.Name,
            Content = new TextBlock
            {
                Text = "此过程不可逆，如需恢复请到 Steam 运行‘验证游戏文件的完整性’。\n\n"
                    + "路径：Steam 游戏库 → 游戏属性 → 已安装文件 → 验证游戏文件的完整性。\n\n"
                    + "当前版本尚未提供可应用的 Mod 补丁，不会修改游戏文件。",
                TextWrapping = TextWrapping.Wrap,
            },
            CloseButtonText = "我知道了",
            DefaultButton = ContentDialogButton.Close,
        };
        await warning.ShowAsync();
    }

    private async Task<FrameworkElement> CreateCoverPreviewAsync(KairoGame game)
    {
        var coverBox = new Border
        {
            Width = 150,
            Height = 210,
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["CoverGradientBrush"],
            Child = new Grid(),
        };
        var coverGrid = (Grid)coverBox.Child;
        var thumb = await CoverService.Default.LoadCoverImageAsync(
            game.AppId,
            game.CoverSaturation);
        if (thumb != null)
        {
            coverGrid.Children.Add(new Image { Source = thumb, Stretch = Stretch.UniformToFill });
        }
        else
        {
            coverGrid.Children.Add(new TextBlock
            {
                Text = game.Name.Length > 0 ? game.Name[..1].ToUpperInvariant() : "?",
                FontSize = 48,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        return coverBox;
    }

    private async Task LaunchGameAsync(KairoGame game)
        => await OpenSteamUriAsync(SteamLinkService.Run(game.AppId), "启动 Steam 游戏");

    private async Task OpenSteamUriAsync(string uri, string action)
        => await OpenExternalAsync(uri, action, "请确认 Steam 已安装、已登录，并允许启动 Steam 链接。");

    private async Task OpenExternalAsync(string target, string action, string errorMessage)
    {
        try
        {
            if (Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }) == null)
                throw new InvalidOperationException("系统未返回启动进程。");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{target} 失败: {ex.Message}");
            var errorDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = $"{action}失败",
                Content = errorMessage,
                CloseButtonText = "关闭",
            };
            await errorDialog.ShowAsync();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text?.Trim() ?? "";
        RebuildView();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }
}
