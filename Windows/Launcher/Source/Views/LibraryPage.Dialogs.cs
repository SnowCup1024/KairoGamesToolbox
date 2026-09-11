using System.Diagnostics;
using KairosoftGameToolbox.Models;
using KairosoftGameToolbox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KairosoftGameToolbox.Views;

// Steam 启动及错误反馈。
public sealed partial class LibraryPage
{
    private async Task LaunchGameAsync(KairoGame game)
        {
        if (!game.IsNonSteam) { await OpenSteamUriAsync(SteamLinkService.Run(game.AppId), L.T("启动 Steam 游戏")); return; }
        try
        {
            if (!GameFolderService.ContainsExecutable(game.InstallDir)) throw new IOException(L.T("游戏目录不可用。"));
            Process.Start(new ProcessStartInfo(Path.Combine(game.InstallDir!, "KairoGames.exe")) { UseShellExecute = true, WorkingDirectory = game.InstallDir });
        }
        catch (Exception ex) { await new ContentDialog { XamlRoot = XamlRoot, Title = L.T("启动失败"), Content = ex.Message, CloseButtonText = L.T("关闭") }.ShowAsync(); }
    }

    private async Task OpenSteamUriAsync(string uri, string action)
        => await OpenExternalAsync(uri, action, L.T("请确认 Steam 已安装、已登录，并允许启动 Steam 链接。"));

    private async Task OpenExternalAsync(string target, string action, string errorMessage)
    {
        try
        {
            if (Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }) == null)
                throw new InvalidOperationException(L.T("系统未返回启动进程。"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{target} 失败: {ex.Message}");
            var errorDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = L.F("{0}失败", action),
                Content = errorMessage,
                CloseButtonText = L.T("关闭"),
            };
            await errorDialog.ShowAsync();
        }
    }

}
