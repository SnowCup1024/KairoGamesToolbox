using KairosoftGameToolbox.Services;
using KairosoftGameToolbox.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace KairosoftGameToolbox;

public sealed partial class MainWindow : Window
{
    private const int MinimumWindowWidth = 1280;
    private const int MinimumWindowHeight = 860;

    public MainWindow()
    {
        InitializeComponent();
        Title = L.T("开罗游戏工具箱");

        // 自绘标题栏 + Mica（跟随设置的浅色/深色主题）
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported()
            ? new DesktopAcrylicBackdrop() : new MicaBackdrop();

        // 默认尺寸 1280×860，按工作区收缩并居中
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var work = area.WorkArea;
        // 1280x860 是目标初始尺寸；小于该尺寸的工作区必须允许窗口收缩，不能把最小值再次夹回自身。
        int w = Math.Min(MinimumWindowWidth, work.Width);
        int h = Math.Min(MinimumWindowHeight, work.Height);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = w;
            presenter.PreferredMinimumHeight = h;
        }

        AppWindow.Resize(new SizeInt32(w, h));
        AppWindow.Move(new PointInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2));

        // 事件接线
        NavView.SelectedItem = LibraryNavItem;
        SettingsPage.SettingsChanged += (_, _) => _ = LibraryPage.RefreshAsync();
        SettingsPage.ThemeChanged += (_, preference) => ApplyTheme(preference);
        SettingsPage.LanguageChanged += (_, _) => ApplyLanguage();

        ApplyTheme(SettingsService.Instance.Current.ThemePreference);
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString();
        bool library = tag == "library";
        bool settings = tag == "settings";
        LibraryPage.Visibility = library ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;
        LibraryPage.SetPageActive(library);
        PageMotion.Enter(library ? LibraryPage : settings ? SettingsPage : AboutPage);
        if (library) _ = LibraryPage.RefreshAsync(); // 回到库页自动重扫，联网封面复用内存缓存
    }

    public void ApplyTheme(string? preference)
    {
        RootGrid.RequestedTheme = preference switch
        {
            "dark" => ElementTheme.Dark,
            "light" => ElementTheme.Light,
            _ => ElementTheme.Default
        };
    }

    private void ApplyLanguage()
    {
        LibraryPage.SetPageActive(false);
        PageHost.Children.Clear();
        LibraryPage = new LibraryPage { Visibility = Visibility.Collapsed };
        SettingsPage = new SettingsPage();
        AboutPage = new AboutPage { Visibility = Visibility.Collapsed };
        PageHost.Children.Add(LibraryPage); PageHost.Children.Add(SettingsPage); PageHost.Children.Add(AboutPage);
        SettingsPage.SettingsChanged += (_, _) => _ = LibraryPage.RefreshAsync();
        SettingsPage.ThemeChanged += (_, preference) => ApplyTheme(preference);
        SettingsPage.LanguageChanged += (_, _) => ApplyLanguage();
        Title = L.T("开罗游戏工具箱"); AppTitle.Text = Title;
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
            item.Content = item.Tag?.ToString() switch { "library" => L.T("游戏库"), "settings" => L.T("设置"), _ => L.T("关于") };
    }
}
