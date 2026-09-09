using System.Diagnostics;
using KairosoftGameToolbox.Models;
using KairosoftGameToolbox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KairosoftGameToolbox.Views;

// 游戏启动、补丁警告及目标选择；页面扫描和列表交互保留在主文件。
public sealed partial class LibraryPage
{
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
            bool nonSteamRequested = false;
            installDialog.Title = CreateLaunchDialogTitle(game, installContent, () =>
            {
                nonSteamRequested = true;
                installDialog.Hide();
            });
            var installResult = await installDialog.ShowAsync();
            if (nonSteamRequested)
            {
                await ShowNonSteamPatchAsync(game);
                return;
            }
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
        bool nonSteamPatchRequested = false;
        dialog.Title = CreateLaunchDialogTitle(game, content, () =>
        {
            nonSteamPatchRequested = true;
            dialog.Hide();
        });
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
        if (nonSteamPatchRequested)
            await ShowNonSteamPatchAsync(game);
        else if (patchRequested)
            await ShowPatchWarningAsync(game);
        else if (launchRequested)
            await LaunchGameAsync(game);
    }

    private static FrameworkElement CreateLaunchDialogTitle(KairoGame game, FrameworkElement content, Action requestNonSteamPatch)
    {
        const string label = "将 Mod 补丁应用于非 Steam 下载版本";
        var moreButton = new Button
        {
            Content = new SymbolIcon(Symbol.More),
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(moreButton, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(moreButton, label);
        moreButton.Click += (_, _) => requestNonSteamPatch();
        var title = new Grid { ColumnSpacing = 12 };
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // ContentDialog 标题容器默认左对齐；跟随正文宽度，确保按钮位于右边缘。
        content.SizeChanged += (_, e) => title.Width = e.NewSize.Width;
        Grid.SetColumn(moreButton, 1);
        title.Children.Add(moreButton);
        var name = new TextBlock
        {
            Text = game.Name,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(name, 0);
        title.Children.Add(name);
        return title;
    }

    private async Task ShowNonSteamPatchAsync(KairoGame game)
    {
        var backupWarning = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = "非 Steam 版本 · " + game.Name,
            Content = new TextBlock
            {
                Text = "Mod 补丁将直接修改所选游戏的原文件，工具箱无法实现文件回滚。请先备份完整游戏目录和存档，并确认备份可用。\n\n"
                    + "非 Steam 下载版本可能与补丁要求的游戏版本或文件结构不同，应用不匹配的补丁可能导致游戏无法启动、文件损坏或存档丢失。\n\n"
                    + "此目标不能依靠 Steam 的文件完整性验证恢复；如需恢复，请使用自行保存的备份。没有可用备份时，请取消操作。\n\n"
                    + "点击“我已知晓并确认”后选择包含 KairoGames.exe 的游戏文件夹。当前版本尚未提供可应用的 Mod 补丁，不会修改游戏文件。",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "我已知晓并确认",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        // 显式给确认按钮设置危险操作样式，取消仍为默认焦点，避免回车误确认。
        var dangerStyle = new Style(typeof(Button))
        {
            BasedOn = (Style)Application.Current.Resources["DefaultButtonStyle"],
        };
        // 与默认强调按钮一致，背景覆盖边框外缘，避免约 1px 的视觉内缩。
        dangerStyle.Setters.Add(new Setter(Control.BackgroundSizingProperty, BackgroundSizing.OuterBorderEdge));
        dangerStyle.Setters.Add(new Setter(Control.BackgroundProperty,
            new SolidColorBrush(Windows.UI.Color.FromArgb(255, 235, 0, 0))));
        dangerStyle.Setters.Add(new Setter(Control.ForegroundProperty,
            new SolidColorBrush(Microsoft.UI.Colors.White)));
        backupWarning.PrimaryButtonStyle = dangerStyle;
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed" })
            backupWarning.Resources[key] = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 210, 0, 0));
        foreach (var key in new[] { "ButtonForegroundPointerOver", "ButtonForegroundPressed" })
            backupWarning.Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.White);
        if (await backupWarning.ShowAsync() != ContentDialogResult.Primary) return;

        string? executablePath;
        try
        {
            var window = App.MainWindowInstance
                ?? throw new InvalidOperationException("主窗口不可用。");
            var picker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
                CommitButtonText = "选择游戏文件夹",
            };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(window));
            var folder = await picker.PickSingleFolderAsync();
            if (folder == null) return;
            if (!GameFolderService.ContainsExecutable(folder.Path))
                throw new IOException("所选文件夹中未找到 KairoGames.exe。");
            executablePath = Path.Combine(folder.Path, "KairoGames.exe");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"选择非 Steam 游戏失败: {ex.Message}");
            var error = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = "无法选择游戏文件夹",
                Content = "请选择直接包含 KairoGames.exe 的游戏文件夹；若无法打开选择器，请确认目录可以访问后重试。",
                CloseButtonText = "关闭",
            };
            await error.ShowAsync();
            return;
        }
        // 复用 Steam 补丁入口；目标来自用户选择，不运行 EXE，也不改写 Steam 游戏路径。
        await ShowPatchWarningAsync(game, executablePath);
    }

    private async Task ShowPatchWarningAsync(KairoGame game, string? executablePath = null)
    {
        var warning = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = "应用 Mod 补丁 · " + game.Name,
            Content = new TextBlock
            {
                Text = (executablePath == null
                    ? "工具箱无法实现文件回滚，如需恢复请到 Steam 运行‘验证游戏文件的完整性’。\n\n"
                        + "路径：Steam 游戏库 → 游戏属性 → 已安装文件 → 验证游戏文件的完整性。\n\n"
                    : "工具箱无法实现文件回滚，如需恢复请使用你自行保存的备份。\n\n"
                        + "目标 EXE：" + executablePath + "\n\n")
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

}
