using KairosoftGameToolbox.Models;
using KairosoftGameToolbox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace KairosoftGameToolbox.Controls;

/// <summary>单张游戏卡片：封面缓存 + 名称 + 安装徽标 + 可选游玩记录。</summary>
public sealed partial class GameCard : UserControl
{
    /// <summary>左键单击卡片（进入游戏详情页）。</summary>
    public event EventHandler<KairoGame>? GameClicked;

    /// <summary>右键菜单「启动」。</summary>
    public event EventHandler<KairoGame>? LaunchRequested;

    /// <summary>右键菜单「打开存档目录」。</summary>
    public event EventHandler<KairoGame>? SaveDirectoryRequested;

    private KairoGame? Game => DataContext as KairoGame;
    private int _loadToken;

    public GameCard()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdateCard();
    }

    private async void UpdateCard()
    {
        var game = Game;
        if (game == null) return;

        int token = ++_loadToken;
        GameName.Text = game.Name;

        // 徽标：已安装绿色 / 已拥有黄色 / 未拥有或状态未知灰色
        if (game.IsInstalled)
        {
            BadgeDot.Fill = (SolidColorBrush)Application.Current.Resources["InstalledDotBrush"];
            BadgeText.Text = "已安装";
        }
        else if (game.Ownership == OwnershipStatus.Owned)
        {
            BadgeDot.Fill = (SolidColorBrush)Application.Current.Resources["OwnedDotBrush"];
            BadgeText.Text = "已拥有";
        }
        else
        {
            BadgeDot.Fill = (SolidColorBrush)Application.Current.Resources["NotOwnedDotBrush"];
            BadgeText.Text = game.LibraryStatusText;
        }

        // 游玩记录（可选）
        if (game.Stats is { } stats)
        {
            var formattedStats = FormatStats(stats);
            PlaytimeText.Text = formattedStats;
            PlaytimeText.Visibility = string.IsNullOrWhiteSpace(formattedStats)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
        else
        {
            PlaytimeText.Text = "";
            PlaytimeText.Visibility = Visibility.Collapsed;
        }

        // 封面
        CoverImage.Source = null;
        CoverImage.Visibility = Visibility.Collapsed;
        Placeholder.Visibility = Visibility.Visible;
        PlaceholderChar.Text = game.Name.Length > 0 ? game.Name[..1].ToUpperInvariant() : "?";

        var bmp = await CoverService.Default.LoadCoverImageAsync(
            game.AppId,
            game.CoverSaturation);
        if (token != _loadToken || bmp == null) return; // 卡片已被复用或加载失败
        CoverImage.Source = bmp;
        CoverImage.Visibility = Visibility.Visible;
        Placeholder.Visibility = Visibility.Collapsed;
    }

    private static string FormatStats(GamePlayStats stats)
    {
        var parts = new List<string>();
        double hours = stats.PlaytimeForeverMinutes / 60.0;
        if (hours >= 1)
            parts.Add($"已玩 {hours:0.#} 小时");
        else if (stats.PlaytimeForeverMinutes > 0)
            parts.Add($"已玩 {stats.PlaytimeForeverMinutes} 分钟");

        if (stats.LastPlayedUnix is { } last)
        {
            try
            {
                var when = DateTimeOffset.FromUnixTimeSeconds(last);
                var ago = DateTimeOffset.Now - when;
                if (ago.TotalMinutes < 60)
                    parts.Add($"{(int)Math.Max(1, ago.TotalMinutes)} 分钟前游玩");
                else if (ago.TotalHours < 24)
                    parts.Add($"{(int)ago.TotalHours} 小时前游玩");
                else
                    parts.Add($"{(int)ago.TotalDays} 天前游玩");
            }
            catch
            {
                // 非法时间戳忽略
            }
        }
        return string.Join(" · ", parts);
    }

    private void RootBorder_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (Game is { } game) GameClicked?.Invoke(this, game);
    }

    private void MenuMod_Click(object sender, RoutedEventArgs e)
    {
        if (Game is { } game) GameClicked?.Invoke(this, game);
    }

    private void MenuLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (Game is { } game) LaunchRequested?.Invoke(this, game);
    }

    private void CardMenuFlyout_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout) return;
        var saveItem = flyout.Items
            .OfType<MenuFlyoutItem>()
            .FirstOrDefault(item => item.Text == "打开存档目录");
        if (saveItem == null) return;

        var game = Game;
        saveItem.IsEnabled = game?.IsInstalled == true
            && !string.IsNullOrWhiteSpace(game.SaveDir)
            && Directory.Exists(game.SaveDir);
    }

    private void MenuOpenSaveDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (Game is { } game && game.IsInstalled && !string.IsNullOrWhiteSpace(game.SaveDir))
            SaveDirectoryRequested?.Invoke(this, game);
    }

    private void RootBorder_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        HoverOutStoryboard.Stop();
        HoverInStoryboard.Begin();
    }

    private void RootBorder_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        HoverInStoryboard.Stop();
        HoverOutStoryboard.Begin();
    }
}
