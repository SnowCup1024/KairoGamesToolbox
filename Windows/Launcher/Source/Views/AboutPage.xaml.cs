using KairosoftGameToolbox.Services;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KairosoftGameToolbox.Views;

/// <summary>
/// 关于页：显示版本、当前更新日志和许可证摘要。
/// </summary>
public sealed partial class AboutPage : UserControl
{
    private const string LicenseFileName = "LICENSE";

    private bool _showInternalVersion;

    private void VersionText_Click(object sender, RoutedEventArgs e)
    {
        _showInternalVersion = !_showInternalVersion;
        VersionText.Content = _showInternalVersion ? "v" + ReleaseInfo.Version : ReleaseInfo.DisplayVersion;
    }

    public AboutPage()
    {
        InitializeComponent();
        Services.PageMotion.Constrain(PageScroll, PageBody);
        Loaded += (_, _) => LoadAboutInfo();
    }

    private void LoadAboutInfo()
    {
        VersionText.Content = _showInternalVersion ? "v" + ReleaseInfo.Version : ReleaseInfo.DisplayVersion;
        ChangelogContent.Children.Clear();
        foreach (var raw in ReleaseInfo.Changelog.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("# ")) continue;
            var heading = line.StartsWith("## ");
            var bullet = line.StartsWith("- ");
            var text = heading ? line[3..] : bullet ? line[2..] : line;
            var block = new TextBlock
            {
                Text = (bullet ? "• " : "") + L.T(text),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            };
            if (heading) block.Style = (Style)Application.Current.Resources["LabelStyle"];
            ChangelogContent.Children.Add(block);
        }
    }

    private static string ReadBundledText(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, relativePath);
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch
        {
            return "";
        }
    }

    private async void ViewLicense_Click(object sender, RoutedEventArgs e)
    {
        var text = ReadBundledText(LicenseFileName);
        await ShowFullTextAsync("GNU General Public License v3.0", text, L.T("许可证全文暂不可用。"));
    }

    private async Task ShowFullTextAsync(string title, string text, string fallback)
    {
        var body = string.IsNullOrWhiteSpace(text) ? fallback : text;
        var viewer = new ScrollViewer
        {
            MaxHeight = 560,
            Content = new TextBlock
            {
                Text = body,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
            },
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = title,
            Content = viewer,
            CloseButtonText = L.T("关闭"),
        };
        await dialog.ShowAsync();
    }
}
