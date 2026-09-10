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
    private string? _preferredSteamId;
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
        catch (Exception ex)
        {
            await new ContentDialog { XamlRoot = XamlRoot, Title = "游戏库刷新失败", Content = ex.Message, CloseButtonText = "关闭" }.ShowAsync();
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
        var preferredSteamId = SteamAccountService.ResolveSteamId(_steam);
        _preferredSteamId = preferredSteamId;
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

        foreach (var local in new NonSteamLibraryService().Load())
        {
            var game = list.FirstOrDefault(g => g.AppId == local.AppId);
            if (game == null || !GameFolderService.ContainsExecutable(local.Directory)) continue;
            try { if (NonSteamLibraryService.Identify(local.Directory, _catalog).AppId != game.AppId) continue; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { continue; }
            game.NonSteamDirectory = local.Directory;
            if (!game.IsInstalled)
            {
                game.InstallDir = local.Directory;
                game.IsInstalled = true;
                game.SaveDir = SaveDirectoryService.Find(local.Directory, null);
            }
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
            PinyinName = _catalog.TryGetPinyinName(appId),
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
        var steamId = _preferredSteamId;
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

        InstalledSection.Visibility = Visibility.Visible;
        if (!sections.ContainsKey(GameSectionKind.Installed)) InstalledSectionTitle.Text = "已安装 · 0款";
        bool empty = filtered.Count == 0;
        GridScroll.Visibility = Visibility.Visible;
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

    private void Card_GameClicked(object sender, KairoGame game)
    {
        var page = new GameDetailsPage(game);
        page.BackRequested += async (_, _) =>
        {
            DetailHost.Content = null;
            DetailHost.Visibility = Visibility.Collapsed;
            LibraryRoot.Visibility = Visibility.Visible;
            await RefreshAsync();
        };
        DetailHost.Content = page;
        LibraryRoot.Visibility = Visibility.Collapsed;
        DetailHost.Visibility = Visibility.Visible;
    }

    private async void Card_LaunchRequested(object sender, KairoGame game)
    {
        if (game.IsInstalled)
            await LaunchGameAsync(game);
        else
            Card_GameClicked(sender, game);
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

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text?.Trim() ?? "";
        RebuildView();
    }

    private async void AddNonSteam_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
            var selected = await picker.PickSingleFolderAsync();
            if (selected == null) return;
            var entry = await Task.Run(() => NonSteamLibraryService.Identify(selected.Path, _catalog));
            new NonSteamLibraryService().Save(entry.AppId, selected.Path);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await new ContentDialog { XamlRoot = XamlRoot, Title = "添加非 Steam 游戏未完成", Content = ex.Message, CloseButtonText = "关闭" }.ShowAsync();
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }
}
