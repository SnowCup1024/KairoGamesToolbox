using Microsoft.UI.Xaml;

namespace KairosoftGameToolbox;

public partial class App : Application
{
    /// <summary>主窗口实例（设置页等通过它切换主题 / 触发重扫）。</summary>
    public static MainWindow? MainWindowInstance { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindowInstance = new MainWindow();
        MainWindowInstance.Activate();
    }
}
