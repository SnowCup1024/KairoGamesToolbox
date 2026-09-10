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
        instanceLease = Services.LauncherInstanceLease.TryAcquire();
        if (instanceLease == null)
        {
            Services.ExistingLauncherWindow.Activate();
            Exit();
            return;
        }
        if (Services.ExistingLauncherWindow.Activate())
        {
            instanceLease.Dispose();
            instanceLease = null;
            Exit();
            return;
        }
        Services.SettingsService.Instance.InitializeSteamPath();
        MainWindowInstance = new MainWindow();
        MainWindowInstance.Activate();
    }
}
