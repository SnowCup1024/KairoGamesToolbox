using System.Diagnostics;
using KairosoftGameToolbox.Models;
using KairosoftGameToolbox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace KairosoftGameToolbox.Views;

public sealed partial class GameDetailsPage : UserControl
{
    private readonly KairoGame game;
    private string? target;
    private bool nonSteam;
    private bool busy;
    private string FolderName => ModPackageService.GameFolder(game.EnglishName);
    public event EventHandler? BackRequested;

    public GameDetailsPage(KairoGame game)
    {
        InitializeComponent();
        this.game = game;
        GameTitle.Text = game.Name;
        GameStatus.Text = $"{game.LibraryStatusText} · AppID {game.AppId}";
        target = game.NonSteamDirectory ?? game.InstallDir;
        nonSteam = game.NonSteamDirectory != null;
        SteamButton.IsEnabled = game.IsInstalled && !game.IsNonSteam;
        InitializeControls();
        UpdateTarget();
        Loaded += async (_, _) => Cover.Source = await CoverService.Default.LoadCoverImageAsync(game.AppId, 1);
    }

    private void DetailsScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        PageBody.Width = Math.Max(0, Math.Min(1050, e.NewSize.Width - 56));
        TargetText.MaxWidth = Math.Max(40, PageBody.Width - 96);
        DirectoryButtons.Orientation = PageBody.Width < 500 ? Orientation.Vertical : Orientation.Horizontal;
        Grid.SetColumn(ConnectionHeader, PageBody.Width < 500 ? 0 : 1);
        Grid.SetRow(ConnectionHeader, PageBody.Width < 500 ? 1 : 0);
        ConnectionHeader.HorizontalAlignment = PageBody.Width < 500 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    private void UpdateTarget()
    {
        TargetText.Text = string.IsNullOrWhiteSpace(target) ? L.T("尚未选择游戏目录") : (nonSteam ? L.T("非 Steam · ") : "Steam · ") + target;
        InstallButton.IsEnabled = BundledModService.ForGame(game.AppId) != null && GameFolderService.ContainsExecutable(target);
        LaunchButton.Content = nonSteam || game.IsInstalled ? L.T("启动游戏") : L.T("打开 Steam 商店");
        var selected = GameFolderService.ContainsExecutable(target);
        GameStatus.Text = $"{(selected && nonSteam ? L.T("已安装 · 非 Steam") : game.LibraryStatusText)} · AppID {game.AppId}";
        ModSection.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        RemoveGameButton.Visibility = nonSteam ? Visibility.Visible : Visibility.Collapsed;
        var installed = selected && ModPackageService.HasInstalledMod(target!);
        InstallButton.Content = installed ? L.T("更新 Mod") : L.T("安装 Mod");
        RemoveModButton.IsEnabled = installed;
        InstalledModText.Text = installed ? L.T("已安装 Mod") : L.T("未安装 Mod");
        ResultText.Text = "";
        RestartControls();
    }

    private void Back_Click(object sender, RoutedEventArgs e) { if (!busy) BackRequested?.Invoke(this, EventArgs.Empty); }
    private void Steam_Click(object sender, RoutedEventArgs e) { target = game.InstallDir; nonSteam = false; UpdateTarget(); }
    private void InitializePicker(object picker) => WinRT.Interop.InitializeWithWindow.Initialize(picker,
        WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));

    private async Task RunAsync(Func<Task> operation)
    {
        if (busy) return;
        busy = true;
        IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        try { await operation(); }
        catch (Exception ex) { ResultText.Text = L.T("操作未完成：") + ex.Message; }
        finally { busy = false; IsEnabled = true; Progress.Visibility = Visibility.Collapsed; }
    }

    private async void Folder_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder, CommitButtonText = L.T("选择游戏目录") };
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);
        var selected = await picker.PickSingleFolderAsync();
        if (selected == null) return;
        var catalog = new AppIdCatalog(Path.Combine(AppContext.BaseDirectory, "Data", "KairosoftGames.json"));
        var identity = await Task.Run(() => NonSteamLibraryService.Identify(selected.Path, catalog));
        if (identity.AppId != game.AppId) throw new IOException(L.T("所选目录属于其他游戏：") + identity.ChineseName);
        new NonSteamLibraryService().Save(game.AppId, selected.Path);
        target = selected.Path;
        nonSteam = true;
        UpdateTarget();
    });

    private void RemoveGame_Click(object sender, RoutedEventArgs e)
    {
        if (!nonSteam || busy) return;
        new NonSteamLibraryService().Remove(game.AppId);
        game.NonSteamDirectory = null;
        if (game.IsNonSteam || string.IsNullOrEmpty(game.LibraryPath)) { game.IsInstalled = false; game.InstallDir = null; }
        target = game.InstallDir; nonSteam = false; UpdateTarget();
    }

    private static void CheckGameStopped()
    {
        var running = Process.GetProcessesByName("KairoGames");
        try { if (running.Length > 0) throw new IOException(L.T("请先退出正在运行的开罗游戏。")); }
        finally { foreach (var process in running) process.Dispose(); }
    }

    private async void RemoveMod_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (target == null) return;
        var confirm = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = L.T("删除 Mod"),
            Content = L.T("删除已识别的模组文件，保留游戏原文件与存档。存档中已经生效的修改不会撤销。"),
            PrimaryButtonText = L.T("删除 Mod"), CloseButtonText = L.T("取消"), DefaultButton = ContentDialogButton.Close };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        CheckGameStopped();
        await Task.Run(() => ModPackageService.Uninstall(target, game.AppId, FolderName));
        ModSessionCache.Clear(game.AppId, target);
        UpdateTarget();
        ResultText.Text = L.T("Mod 已删除。");
    });

    private async void Install_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (target == null) return;
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme,
            Title = L.T("安装 Mod") + " · " + game.Name,
            Content = L.T("将下载通用运行组件并安装内置模组。请先备份存档。删除 Mod 可恢复原游戏启动方式，但不会撤销存档中的修改。") + "\n\n" + target,
            PrimaryButtonText = L.T("我已知晓并确认"), CloseButtonText = L.T("取消"), DefaultButton = ContentDialogButton.Close,
        };
        var danger = new Style(typeof(Button)) { BasedOn = (Style)Application.Current.Resources["DefaultButtonStyle"] };
        danger.Setters.Add(new Setter(Control.BackgroundProperty, new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 235, 0, 0))));
        danger.Setters.Add(new Setter(Control.ForegroundProperty, new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)));
        danger.Setters.Add(new Setter(Control.BackgroundSizingProperty, BackgroundSizing.OuterBorderEdge));
        confirm.PrimaryButtonStyle = danger;
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        CheckGameStopped();
        await BundledModService.InstallAsync(game.AppId, target);
        UpdateTarget();
        ResultText.Text = L.T("Mod 已安装，请启动游戏并打开模组控制页。");
    });

    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private async void Launch_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        if (nonSteam)
        {
            if (target == null || !GameFolderService.ContainsExecutable(target)) throw new IOException(L.T("游戏目录已不可用。"));
            Process.Start(new ProcessStartInfo(Path.Combine(target, "KairoGames.exe")) { UseShellExecute = true, WorkingDirectory = target });
        }
        else Open(game.IsInstalled ? SteamLinkService.Run(game.AppId) : SteamLinkService.Store(game.AppId));
        return Task.CompletedTask;
    });
}
