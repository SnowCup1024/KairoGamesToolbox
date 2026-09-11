using KairosoftGameToolbox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KairosoftGameToolbox.Views;

public sealed partial class GameDetailsPage
{
    private readonly DispatcherTimer controlTimer = new();
    private readonly SemaphoreSlim controlGate = new(1, 1);
    private readonly List<ToggleSwitch> featureSwitches = new();
    private CancellationTokenSource? controlLifetime;
    private bool pageActive = true;
    private bool connected;
    private bool ready;
    private bool confirmedEnabled;
    private bool updatingSwitch;
    private bool commandPending;
    private bool controlError;

    private void InitializeControls()
    {
        foreach (var feature in ModFeatures.ForGame(game.AppId))
        {
            var label = new TextBlock { Text = feature.Name, MaxWidth = 140, VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["LabelStyle"] };
            ToolTipService.SetToolTip(label, feature.Description);
            var toggle = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0, IsEnabled = false, VerticalAlignment = VerticalAlignment.Center };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, feature.Name);
            toggle.Toggled += Feature_Toggled;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(label);
            row.Children.Add(toggle);
            FeatureGrid.Children.Add(new Border { Child = row, Padding = new Thickness(12), CornerRadius = new CornerRadius(8) });
            featureSwitches.Add(toggle);
        }
        FeatureGrid.SizeChanged += (_, _) => ArrangeFeatures();
        controlTimer.Tick += async (_, _) => await RefreshControlAsync();
        Loaded += (_, _) => { PageMotion.Enter(PageBody); RestartControls(); };
        Unloaded += (_, _) => StopControls();
    }

    public void SetPageActive(bool active)
    {
        pageActive = active;
        if (active && IsLoaded) RestartControls();
        else StopControls();
    }

    private void ArrangeFeatures()
    {
        int columns = ModFeatures.Columns(FeatureGrid.Children.Count, FeatureGrid.ActualWidth);
        FeatureGrid.ColumnDefinitions.Clear();
        FeatureGrid.RowDefinitions.Clear();
        for (int i = 0; i < columns; i++) FeatureGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < (FeatureGrid.Children.Count + columns - 1) / columns; i++) FeatureGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < FeatureGrid.Children.Count; i++)
        {
            Grid.SetColumn((FrameworkElement)FeatureGrid.Children[i], i % columns);
            Grid.SetRow((FrameworkElement)FeatureGrid.Children[i], i / columns);
        }
    }

    private void StopControls()
    {
        controlTimer.Stop();
        controlLifetime?.Cancel();
    }

    private void RestartControls()
    {
        StopControls();
        controlLifetime?.Dispose();
        controlLifetime = new CancellationTokenSource();
        connected = ready = confirmedEnabled = commandPending = controlError = false;
        ControlFeedback.Text = "";
        UpdateControlUi();
        if (IsLoaded && pageActive)
        {
            controlTimer.Interval = TimeSpan.FromSeconds(15);
            controlTimer.Start();
            _ = RefreshControlAsync();
        }
    }

    private bool CanConnect() => ModFeatures.CanConnect(game.AppId, target);

    private void UpdateControlUi()
    {
        bool installed = CanConnect();
        RefreshControlButton.IsEnabled = installed && !commandPending;
        ConnectionStatus.Text = featureSwitches.Count == 0 ? "暂无修改项"
            : !installed ? "未安装 Mod" : !connected ? "未连接" : ready ? "已连接" : "已连接 · 待启用";
        updatingSwitch = true;
        try
        {
            foreach (var toggle in featureSwitches)
            {
                if (toggle.IsOn != confirmedEnabled) toggle.IsOn = confirmedEnabled;
                toggle.IsEnabled = installed && connected && !controlError && !commandPending;
            }
        }
        finally { updatingSwitch = false; }
        FeatureEmpty.Visibility = featureSwitches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RefreshControl_Click(object sender, RoutedEventArgs e) => await RefreshControlAsync();

    private async void Feature_Toggled(object sender, RoutedEventArgs e)
    {
        if (updatingSwitch) return;
        bool desired = ((ToggleSwitch)sender).IsOn;
        UpdateControlUi();
        await RefreshControlAsync(desired);
    }

    private async Task RefreshControlAsync(bool? desired = null)
    {
        var session = controlLifetime;
        if (session == null || session.IsCancellationRequested || !pageActive || target == null) return;
        var token = session.Token;
        string directory = target;
        bool entered = false;
        try
        {
            if (desired.HasValue) { await controlGate.WaitAsync(token); entered = true; }
            else entered = await controlGate.WaitAsync(0, token);
            if (!entered) return;
            controlTimer.Stop();
            if (!CanConnect())
            {
                connected = ready = false;
                UpdateControlUi();
                return;
            }
            if (desired.HasValue)
            {
                commandPending = true;
                UpdateControlUi();
                if (!ready)
                {
                    var confirm = new ContentDialog
                    {
                        XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "启用模组控制",
                        Content = "请先进入游戏存档并看到余额，再确认启用。不要在游戏首页启用。",
                        PrimaryButtonText = "我已进入存档", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
                    };
                    if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
                    token.ThrowIfCancellationRequested();
                    if (!CanConnect()) return;
                    var activation = await ModControlClient.SendAsync(game.AppId, directory, "activate", cancellationToken: token);
                    if (!activation.Ready || activation.Error != null) throw new IOException(activation.Error ?? "游戏端尚未就绪。");
                }
            }
            token.ThrowIfCancellationRequested();
            if (!CanConnect()) return;
            var reply = await ModControlClient.SendAsync(game.AppId, directory, desired.HasValue ? "set" : "status", desired ?? false, token);
            token.ThrowIfCancellationRequested();
            connected = true;
            ready = reply.Ready;
            confirmedEnabled = reply.Enabled;
            controlError = reply.Error != null;
            ControlFeedback.Text = reply.Error ?? "";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            connected = ready = false;
            ControlFeedback.Text = desired.HasValue ? "未收到确认，修改结果未知；请重新连接核对。" : "";
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            if (entered) controlGate.Release();
            if (entered && ReferenceEquals(session, controlLifetime) && !token.IsCancellationRequested)
            {
                commandPending = false;
                UpdateControlUi();
                // 未安装时只复查本地安装标识，不发送连接请求。
                controlTimer.Interval = TimeSpan.FromSeconds(connected ? 150 : 15);
                if (pageActive) controlTimer.Start();
            }
        }
    }
}
