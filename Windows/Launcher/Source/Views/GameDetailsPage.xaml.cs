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
    private string? package;
    private bool nonSteam;
    private bool busy;
    private string FolderName => ModPackageService.GameFolder(game.EnglishName);
    // 单文件程序的 BaseDirectory 是解压目录；Mods 必须跟随实际 EXE。
    private static string ModsRoot => Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "Mods");
    public event EventHandler? BackRequested;

    public GameDetailsPage(KairoGame game)
    {
        InitializeComponent();
        this.game = game;
        GameTitle.Text = game.Name;
        GameStatus.Text = $"{game.LibraryStatusText} · AppID {game.AppId}";
        target = game.InstallDir;
        SteamButton.IsEnabled = game.IsInstalled;
        UpdateTarget();
        Loaded += async (_, _) => Cover.Source = await CoverService.Default.LoadCoverImageAsync(game.AppId, 1);
    }

    private void UpdateTarget()
    {
        TargetText.Text = string.IsNullOrWhiteSpace(target) ? "尚未选择游戏目录" : (nonSteam ? "非 Steam · " : "Steam · ") + target;
        InstallButton.IsEnabled = package != null && !string.IsNullOrWhiteSpace(target);
        LaunchButton.Content = nonSteam || game.IsInstalled ? "启动游戏" : "打开 Steam 商店";
        ResultText.Text = "";
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
        catch (Exception ex) { ResultText.Text = "操作未完成：" + ex.Message; }
        finally { busy = false; IsEnabled = true; Progress.Visibility = Visibility.Collapsed; }
    }

    private async void Folder_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder, CommitButtonText = "选择游戏目录" };
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);
        var selected = await picker.PickSingleFolderAsync();
        if (selected == null) return;
        if (!GameFolderService.ContainsExecutable(selected.Path)) throw new IOException("所选目录中未找到 KairoGames.exe。");
        target = selected.Path;
        nonSteam = true;
        UpdateTarget();
    });

    private async void Package_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(".zip");
        InitializePicker(picker);
        var selected = await picker.PickSingleFileAsync();
        if (selected == null) return;
        var imported = await Task.Run(() => ModPackageService.Import(selected.Path, ModsRoot, game.AppId, FolderName));
        var manifest = await Task.Run(() => ModPackageService.Read(imported, game.AppId, FolderName));
        package = imported;
        PackageText.Text = $"{manifest.Channel} · v{manifest.Version}\n{package}";
        PackageDescription.Text = manifest.Description;
        UpdateTarget();
    });

    private async void Install_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (package == null || target == null) return;
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme,
            Title = "释放 Mod · " + game.Name,
            Content = "模组会向游戏目录安装可执行代码。请仅使用可信来源的 ZIP，并先备份游戏目录和存档。\n\n"
                + (nonSteam ? "非 Steam 版本无法依靠 Steam 验证恢复，请确认备份可用。\n\n" : "Steam 验证不会自动删除新增的模组文件。\n\n")
                + "工具箱暂不提供卸载或文件回滚；已有不同内容的文件将拒绝覆盖。\n\n目标：" + target,
            PrimaryButtonText = "我已知晓并确认", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close,
        };
        var danger = new Style(typeof(Button)) { BasedOn = (Style)Application.Current.Resources["DefaultButtonStyle"] };
        danger.Setters.Add(new Setter(Control.BackgroundProperty, new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 235, 0, 0))));
        danger.Setters.Add(new Setter(Control.ForegroundProperty, new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)));
        danger.Setters.Add(new Setter(Control.BackgroundSizingProperty, BackgroundSizing.OuterBorderEdge));
        confirm.PrimaryButtonStyle = danger;
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        await Task.Run(() =>
        {
            var running = Process.GetProcessesByName("KairoGames");
            try { if (running.Length > 0) throw new IOException("请先退出正在运行的开罗游戏，再释放 Mod。"); }
            finally { foreach (var process in running) process.Dispose(); }
            ModPackageService.Install(package, target, game.AppId, FolderName);
        });
        ResultText.Text = "校验通过，Mod 已释放。请正常启动游戏，并按模组说明操作。";
    });

    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private async void Launch_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        if (nonSteam)
        {
            if (target == null || !GameFolderService.ContainsExecutable(target)) throw new IOException("游戏目录已不可用。");
            Process.Start(new ProcessStartInfo(Path.Combine(target, "KairoGames.exe")) { UseShellExecute = true, WorkingDirectory = target });
        }
        else Open(game.IsInstalled ? SteamLinkService.Run(game.AppId) : SteamLinkService.Store(game.AppId));
        return Task.CompletedTask;
    });
    private async void Mods_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        var path = Path.Combine(ModsRoot, FolderName);
        Directory.CreateDirectory(Path.Combine(path, "Alpha"));
        Directory.CreateDirectory(Path.Combine(path, "Beta"));
        Open(path);
        return Task.CompletedTask;
    });
    private async void Saves_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        var path = nonSteam && target != null ? SaveDirectoryService.Find(target, null) : game.SaveDir;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) throw new IOException("未找到存档目录，请先在游戏中建立存档。");
        Open(path);
        return Task.CompletedTask;
    });
}
