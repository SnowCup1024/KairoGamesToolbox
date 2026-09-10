using KairosoftGameToolbox.Models;
using KairosoftGameToolbox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KairosoftGameToolbox.Views;

public sealed partial class ModControlPage : UserControl
{
    private readonly KairoGame game;
    private readonly string directory;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool busy;
    private bool updating;
    private bool confirmedEnabled;
    private bool active;
    public event EventHandler? BackRequested;

    public ModControlPage(KairoGame game, string directory)
    {
        InitializeComponent();
        this.game = game;
        this.directory = directory;
        Heading.Text = game.Name + " · 修改";
        TargetLabel.Text = directory;
        timer.Tick += async (_, _) => await RequestAsync("status");
        Loaded += async (_, _) => { active = true; timer.Start(); await RequestAsync("status"); };
        Unloaded += (_, _) => { active = false; timer.Stop(); };
    }
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RequestAsync("status");
    private async void Activate_Click(object sender, RoutedEventArgs e) => await RequestAsync("activate");
    private async void Money_Toggled(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        var desired = MoneySwitch.IsOn;
        updating = true;
        MoneySwitch.IsOn = confirmedEnabled;
        updating = false;
        await RequestAsync("set", desired);
    }
    private async Task RequestAsync(string action, bool enabled = false)
    {
        if (busy || !active) return;
        busy = true;
        MoneySwitch.IsEnabled = false;
        ActivateButton.IsEnabled = false;
        try
        {
            var reply = await ModControlClient.SendAsync(game.AppId, directory, action, enabled);
            if (!active) return;
            confirmedEnabled = reply.Enabled;
            updating = true;
            MoneySwitch.IsOn = reply.Enabled;
            updating = false;
            MoneySwitch.OnContent = "已开启";
            MoneySwitch.OffContent = "已关闭";
            Status.Text = reply.Ready ? "已连接 · 游戏端控制已就绪" : "已连接 · 等待进入存档后启用";
            Feedback.Text = reply.Error ?? (action == "set" ? "游戏已确认：金钱反加" + (reply.Enabled ? "开启" : "关闭") : "状态来自游戏端确认。");
            MoneySwitch.IsEnabled = reply.Ready && reply.Error == null;
            ActivateButton.IsEnabled = !reply.Ready && reply.Error == null;
        }
        catch (Exception ex)
        {
            if (!active) return;
            Status.Text = "未连接 · 状态未知";
            MoneySwitch.OnContent = "状态未知";
            MoneySwitch.OffContent = "状态未知";
            Feedback.Text = action == "set" ? "未收到确认，操作结果未知；恢复连接后核对状态。" : "请确认目标游戏已启动并加载测试模组 0.0.4。";
            if (ex is not OperationCanceledException && ex is not TimeoutException) Feedback.Text += " " + ex.Message;
        }
        finally { busy = false; }
    }
}
