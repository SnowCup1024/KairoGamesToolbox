using KairosoftGameToolbox.Models;
using KairosoftGameToolbox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KairosoftGameToolbox.Views;

public sealed partial class ModControlPage : UserControl
{
    private readonly KairoGame game;
    private readonly string directory;
    private readonly DispatcherTimer timer = new();
    private readonly SemaphoreSlim requestGate = new(1, 1);
    private CancellationTokenSource? lifetime;
    private bool updating;
    private bool confirmedEnabled;
    private bool connected;
    private bool ready;
    private bool hasError;
    private bool commandPending;
    public event EventHandler? BackRequested;

    public ModControlPage(KairoGame game, string directory)
    {
        InitializeComponent();
        this.game = game;
        this.directory = directory;
        Heading.Text = game.Name;
        TargetLabel.Text = directory;
        timer.Tick += async (_, _) => await RequestAsync("status");
        Loaded += async (_, _) =>
        {
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = new CancellationTokenSource();
            await RequestAsync("status");
        };
        Unloaded += (_, _) => { lifetime?.Cancel(); timer.Stop(); };
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RequestAsync("status");
    private async void Activate_Click(object sender, RoutedEventArgs e) => await RequestAsync("activate");
    private async void Money_Toggled(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        var desired = MoneySwitch.IsOn;
        SetConfirmedSwitch();
        await RequestAsync("set", desired);
    }

    private void SetConfirmedSwitch()
    {
        if (MoneySwitch.IsOn == confirmedEnabled) return;
        updating = true;
        try { MoneySwitch.IsOn = confirmedEnabled; }
        finally { updating = false; }
    }

    private void UpdateControls()
    {
        MoneySwitch.IsEnabled = connected && ready && !hasError && !commandPending;
        ActivateButton.IsEnabled = connected && !ready && !hasError && !commandPending;
    }

    private async Task RequestAsync(string action, bool enabled = false)
    {
        if (lifetime == null || lifetime.IsCancellationRequested) return;
        var token = lifetime.Token;
        bool command = action != "status";
        if (command && commandPending) return;
        bool entered = false;
        if (command)
        {
            commandPending = true;
            UpdateControls();
        }
        try
        {
            if (command) { await requestGate.WaitAsync(token); entered = true; }
            else entered = await requestGate.WaitAsync(0, token);
            if (!entered) return;
            timer.Stop();
            var reply = await ModControlClient.SendAsync(game.AppId, directory, action, enabled, token);
            token.ThrowIfCancellationRequested();
            connected = true;
            ready = reply.Ready;
            hasError = reply.Error != null;
            confirmedEnabled = reply.Enabled;
            SetConfirmedSwitch();
            Status.Text = reply.Ready ? "已连接 · 游戏端控制已就绪" : "已连接 · 等待进入存档后启用";
            Feedback.Text = reply.Error ?? "";
            Feedback.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            connected = false;
            ready = false;
            Status.Text = "未连接 · 状态未知";
            Feedback.Text = action == "set" ? "未收到确认，操作结果未知；恢复连接后核对状态。" : "请确认目标游戏已启动并加载模组 0.0.4 或兼容版本。";
            if (ex is not OperationCanceledException && ex is not TimeoutException) Feedback.Text += " " + ex.Message;
            Feedback.Visibility = Visibility.Visible;
        }
        finally
        {
            if (entered) requestGate.Release();
            if (command) commandPending = false;
            if (!token.IsCancellationRequested)
            {
                UpdateControls();
                if (entered)
                {
                    timer.Interval = TimeSpan.FromSeconds(connected ? 150 : 15);
                    timer.Start();
                }
            }
        }
    }
}
