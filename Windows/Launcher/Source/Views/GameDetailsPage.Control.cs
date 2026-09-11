using KairoMods.Protocol;
using KairosoftGameToolbox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KairosoftGameToolbox.Views;

public sealed partial class GameDetailsPage
{
    private readonly DispatcherTimer controlTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer logTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly SemaphoreSlim controlGate = new(1, 1);
    private readonly Dictionary<string, (ToggleSwitch Toggle, Slider Multiplier, TextBlock Value)> featureControls = new();
    private Dictionary<string, FeatureState> confirmed = new();
    private CancellationTokenSource? controlLifetime;
    private bool pageActive = true;
    private bool controlVisible;
    private bool connected;
    private bool updatingSwitch;
    private bool commandPending;
    private bool inputPending;
    private string? gameSession;
    private ModLogReader? logReader;
    private readonly int[] multipliers = { 1, 2, 5, 20 };

    private void InitializeControls()
    {
        foreach (var feature in ModFeatures.ForGame(game.AppId))
        {
            var label = new TextBlock { Text = feature.Name, VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(label, feature.Description);
            var toggle = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0, IsEnabled = false, VerticalAlignment = VerticalAlignment.Center };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, feature.Name);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(toggle); row.Children.Add(label);
            var value = new TextBlock { Text = "1x", Width = 32, VerticalAlignment = VerticalAlignment.Center };
            var slider = new Slider { Minimum = 0, Maximum = 3, StepFrequency = 1, TickFrequency = 1, IsEnabled = false, IsThumbToolTipEnabled = false };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, feature.Name + " " + L.T("倍率"));
            var sliderRow = new Grid { ColumnSpacing = 8 };
            sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); sliderRow.ColumnDefinitions.Add(new ColumnDefinition());
            sliderRow.Children.Add(value); sliderRow.Children.Add(slider); Grid.SetColumn(slider, 1);
            var panel = new StackPanel { Spacing = 8 }; panel.Children.Add(row); panel.Children.Add(sliderRow);
            FeatureGrid.Children.Add(panel);
            featureControls.Add(feature.Id, (toggle, slider, value));
            confirmed[feature.Id] = new();
            toggle.Toggled += async (_, _) => { if (!updatingSwitch) await SendFeatureAsync(feature.Id); };
            slider.ValueChanged += async (_, _) =>
            {
                value.Text = multipliers[(int)slider.Value] + "x";
                if (!updatingSwitch) await SendFeatureAsync(feature.Id);
            };
        }
        FeatureGrid.SizeChanged += (_, _) => ArrangeFeatures();
        controlTimer.Tick += async (_, _) => await RefreshControlAsync();
        logTimer.Tick += (_, _) => ReadLogs();
        Loaded += (_, _) => { PageMotion.Enter(PageBody); UpdateTarget(); };
        Unloaded += (_, _) => StopControls();
    }

    private void ManageTab_Click(object sender, RoutedEventArgs e) => SelectPane(false);
    private void ControlTab_Click(object sender, RoutedEventArgs e) => SelectPane(true);
    private void SelectPane(bool control)
    {
        controlVisible = control;
        ManageTab.IsChecked = !control; ControlTab.IsChecked = control;
        ManagePane.Visibility = control ? Visibility.Collapsed : Visibility.Visible;
        ControlPane.Visibility = control ? Visibility.Visible : Visibility.Collapsed;
        PageMotion.Enter(control ? ControlPane : ManagePane);
        RestartControls();
    }
    public void SetPageActive(bool active)
    {
        pageActive = active;
        if (active && IsLoaded) UpdateTarget(); else StopControls();
    }
    private void ArrangeFeatures()
    {
        int columns = ModFeatures.Columns(FeatureGrid.Children.Count, FeatureGrid.ActualWidth);
        FeatureGrid.ColumnDefinitions.Clear(); FeatureGrid.RowDefinitions.Clear();
        for (int i = 0; i < columns; i++) FeatureGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < (FeatureGrid.Children.Count + columns - 1) / columns; i++) FeatureGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < FeatureGrid.Children.Count; i++)
        {
            var panel = (StackPanel)FeatureGrid.Children[i];
            Grid.SetColumn(panel, i % columns); Grid.SetRow(panel, i / columns);
            var label = (TextBlock)((StackPanel)panel.Children[0]).Children[1];
            label.MaxWidth = Math.Max(40, (FeatureGrid.ActualWidth - 16 * (columns - 1)) / columns - 64);
        }
    }
    private void StopControls()
    {
        controlTimer.Stop(); logTimer.Stop(); controlLifetime?.Cancel();
        connected = false; commandPending = false;
        logReader?.Dispose(); logReader = null;
    }
    private void RestartControls()
    {
        StopControls(); controlLifetime?.Dispose(); controlLifetime = new();
        confirmed = target == null ? confirmed : ModSessionCache.Get(game.AppId, target) ?? featureControls.Keys.ToDictionary(id => id, _ => new FeatureState());
        UpdateControlUi();
        if (IsLoaded && pageActive && controlVisible)
        {
            controlTimer.Start();
            if (CanConnect())
            {
                logReader = new ModLogReader(Path.Combine(target!, "BepInEx", "LogOutput.log"));
                LogLines.Children.Clear(); logTimer.Start(); ReadLogs();
            }
            _ = RefreshControlAsync();
        }
    }
    private bool CanConnect() => ModFeatures.CanConnect(game.AppId, target);
    private void UpdateControlUi()
    {
        bool installed = CanConnect();
        RefreshControlButton.IsEnabled = installed && controlVisible && !commandPending;
        ConnectionStatus.Text = L.T(!installed ? L.T("未安装 Mod") : connected ? L.T("已连接") : L.T("未连接"));
        updatingSwitch = true;
        try
        {
            foreach (var (id, controls) in featureControls)
            {
                var state = confirmed.GetValueOrDefault(id) ?? new();
                if (controls.Toggle.IsOn != state.Enabled) controls.Toggle.IsOn = state.Enabled;
                var position = Array.IndexOf(multipliers, state.Multiplier);
                if (controls.Multiplier.Value != position) controls.Multiplier.Value = position;
                controls.Toggle.IsEnabled = controls.Multiplier.IsEnabled = installed && connected && !commandPending && !inputPending;
            }
        }
        finally { updatingSwitch = false; }
        FeatureEmpty.Visibility = featureControls.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void RefreshControl_Click(object sender, RoutedEventArgs e) => await RefreshControlAsync();
    private async Task SendFeatureAsync(string id)
    {
        if (!connected || commandPending || inputPending) { UpdateControlUi(); return; }
        var controls = featureControls[id];
        var desired = new Dictionary<string, FeatureState>(confirmed) { [id] = new(controls.Toggle.IsOn, multipliers[(int)controls.Multiplier.Value]) };
        inputPending = true;
        UpdateControlUi();
        try { await RefreshControlAsync(desired); }
        finally { inputPending = false; UpdateControlUi(); }
    }
    private async Task RefreshControlAsync(Dictionary<string, FeatureState>? desired = null)
    {
        var session = controlLifetime;
        if (session == null || session.IsCancellationRequested || !pageActive || !controlVisible || target == null) return;
        var token = session.Token; string directory = target; bool entered = false;
        try
        {
            if (desired != null) { await controlGate.WaitAsync(token); entered = true; }
            else entered = await controlGate.WaitAsync(0, token);
            if (!entered) return;
            if (!CanConnect()) { connected = false; return; }
            bool reconnect = !connected;
            if (desired != null) { commandPending = true; UpdateControlUi(); }
            var reply = await ModControlClient.SendFeaturesAsync(game.AppId, directory, desired, token);
            if (reply.Error != null || !reply.Ready) throw new IOException(reply.Error ?? L.T("模组尚未就绪。"));
            if (desired == null && (reconnect || gameSession != reply.Session))
            {
                // Restore only launcher-session memory, never a prior process's persistent settings.
                var restore = ModSessionCache.Get(game.AppId, directory) ?? featureControls.Keys.ToDictionary(id => id, _ => new FeatureState());
                reply = await ModControlClient.SendFeaturesAsync(game.AppId, directory, restore, token);
                if (reply.Error != null || !reply.Ready) throw new IOException(reply.Error ?? L.T("模组尚未就绪。"));
            }
            token.ThrowIfCancellationRequested();
            connected = true; confirmed = new(reply.Features);
            gameSession = reply.Session;
            ModSessionCache.Set(game.AppId, directory, confirmed); ControlFeedback.Text = "";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) { connected = false; ControlFeedback.Text = desired == null ? "" : L.T("未收到确认，请重新连接核对。"); System.Diagnostics.Debug.WriteLine(ex); }
        }
        finally
        {
            if (entered) controlGate.Release();
            if (entered && ReferenceEquals(session, controlLifetime) && !token.IsCancellationRequested) { commandPending = false; UpdateControlUi(); }
        }
    }
    private void ReadLogs()
    {
        if (!controlVisible || !pageActive || !CanConnect() || logReader == null) return;
        try
        {
            bool atEnd = LogScroll.ScrollableHeight - LogScroll.VerticalOffset < 24;
            foreach (var line in logReader.Read())
            {
                var formatted = ModLogReader.Format(line, BundledModService.ForGame(game.AppId)!, L.Language);
                LogLines.Children.Add(new TextBlock { Text = formatted.Text, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12,
                    TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
                    Foreground = new SolidColorBrush(formatted.Error ? Microsoft.UI.Colors.IndianRed : formatted.Resource ? Microsoft.UI.Colors.MediumSeaGreen : ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.LightSteelBlue : Microsoft.UI.Colors.SlateGray) });
            }
            while (LogLines.Children.Count > 500) LogLines.Children.RemoveAt(0);
            if (atEnd) LogScroll.ChangeView(null, double.MaxValue, null, true);
        }
        catch (IOException) { }
    }
}
