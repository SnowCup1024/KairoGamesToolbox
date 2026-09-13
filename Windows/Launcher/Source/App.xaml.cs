using Microsoft.UI.Xaml;

namespace KairosoftGameToolbox;

public partial class App : Application
{
    private Services.LauncherInstanceLease? instanceLease;
    /// <summary>主窗口实例（设置页等通过它切换主题 / 触发重扫）。</summary>
    public static MainWindow? MainWindowInstance { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (Services.ExistingLauncherWindow.HandleExisting())
        {
            Exit();
            return;
        }
        instanceLease = Services.LauncherInstanceLease.TryAcquire();
        if (instanceLease == null)
        {
            // 处理另一个实例在枚举结束后抢先启动的情况。
            if (Services.ExistingLauncherWindow.HandleExisting())
            {
                Exit();
                return;
            }
            instanceLease = Services.LauncherInstanceLease.TryAcquire();
            if (instanceLease == null)
            {
                Exit();
                return;
            }
        }
        Services.SettingsService.Instance.InitializeSteamPath();
        MainWindowInstance = new MainWindow();
        MainWindowInstance.Activate();
    }
}
