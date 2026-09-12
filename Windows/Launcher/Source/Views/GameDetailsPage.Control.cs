using KairoMods.Protocol;
using KairosoftGameToolbox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Documents;
using Windows.ApplicationModel.DataTransfer;

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
    private bool manualRefreshing;
    private readonly DispatcherTimer featureDebounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Dictionary<string, FeatureState>? pendingFeatures;
    private readonly HashSet<Slider> draggingSliders = new();
    private long editRevision;
    private bool sendingFeatures;
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
            var value = new TextBlock { Text = "1x", Width = 28, VerticalAlignment = VerticalAlignment.Center };
            var slider = new Slider { Minimum = 0, Maximum = 3, StepFrequency = 1, TickFrequency = 1,
                IsEnabled = false, IsThumbToolTipEnabled = false, VerticalAlignment = VerticalAlignment.Center };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, feature.Name + " " + L.T("倍率"));
            var panel = new Grid { ColumnSpacing = 8 };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.ColumnDefinitions.Add(new ColumnDefinition());
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            panel.Children.Add(label); panel.Children.Add(toggle); panel.Children.Add(value); panel.Children.Add(slider);
            Grid.SetColumn(toggle, 1); Grid.SetColumn(value, 2); Grid.SetColumn(slider, 3);
            FeatureGrid.Children.Add(panel);
            featureControls.Add(feature.Id, (toggle, slider, value));
            confirmed[feature.Id] = new();
            toggle.Toggled += (_, _) => { if (!updatingSwitch) QueueFeatures(); };
            slider.ValueChanged += (_, _) =>
            {
                value.Text = multipliers[Math.Clamp((int)Math.Round(slider.Value), 0, 3)] + "x";
                if (!updatingSwitch) QueueFeatures();
            };
            slider.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => draggingSliders.Add(slider)), true);
            Microsoft.UI.Xaml.Input.PointerEventHandler released = (_, _) => draggingSliders.Remove(slider);
            slider.AddHandler(UIElement.PointerReleasedEvent, released, true);
            slider.AddHandler(UIElement.PointerCaptureLostEvent, released, true);
            slider.AddHandler(UIElement.PointerCanceledEvent, released, true);
        }
        featureDebounce.Tick += async (_, _) => await SendPendingFeaturesAsync();
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
        double longest = 0;
        foreach (var panel in FeatureGrid.Children.OfType<Grid>())
        {
            var label = (TextBlock)panel.Children[0];
            label.MaxWidth = double.PositiveInfinity;
            label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            longest = Math.Max(longest, label.DesiredSize.Width);
        }
        int columns = ModFeatures.Columns(FeatureGrid.Children.Count, FeatureGrid.ActualWidth);
        if (columns == 2 && (FeatureGrid.ActualWidth - 16) / 2 < longest + 200) columns = 1;
        double labelWidth = Math.Min(longest, Math.Max(40, (FeatureGrid.ActualWidth - 16 * (columns - 1)) / columns - 200));
        FeatureGrid.ColumnDefinitions.Clear(); FeatureGrid.RowDefinitions.Clear();
        for (int i = 0; i < columns; i++) FeatureGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < (FeatureGrid.Children.Count + columns - 1) / columns; i++) FeatureGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < FeatureGrid.Children.Count; i++)
        {
            var panel = (Grid)FeatureGrid.Children[i];
            Grid.SetColumn(panel, i % columns); Grid.SetRow(panel, i / columns);
            var label = (TextBlock)panel.Children[0];
            panel.ColumnDefinitions[0].Width = new GridLength(labelWidth);
            label.MaxWidth = labelWidth;
        }
    }
    private void StopControls()
    {
        controlTimer.Stop(); logTimer.Stop(); featureDebounce.Stop(); controlLifetime?.Cancel();
        pendingFeatures = null; draggingSliders.Clear(); editRevision++;
        connected = false; commandPending = false; manualRefreshing = false;
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
                LogLines.Blocks.Clear(); logTimer.Start(); ReadLogs();
            }
            _ = RefreshControlAsync();
        }
    }
    private bool CanConnect() => ModFeatures.CanConnect(game.AppId, target);
    private void UpdateControlUi()
    {
        bool installed = CanConnect();
        RefreshControlButton.IsEnabled = installed && controlVisible && !commandPending && !manualRefreshing;
        ConnectionStatus.Text = L.T(!installed ? "未安装 Mod" : manualRefreshing ? "正在连接…" : connected ? "已连接" : "未连接");
        updatingSwitch = true;
        try
        {
            foreach (var (id, controls) in featureControls)
            {
                var state = (pendingFeatures ?? confirmed).GetValueOrDefault(id) ?? new();
                if (controls.Toggle.IsOn != state.Enabled) controls.Toggle.IsOn = state.Enabled;
                var position = Array.IndexOf(multipliers, state.Multiplier);
                if (controls.Multiplier.Value != position) controls.Multiplier.Value = position;
                controls.Toggle.IsEnabled = controls.Multiplier.IsEnabled = installed && connected;
            }
        }
        finally { updatingSwitch = false; }
        FeatureEmpty.Visibility = featureControls.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void RefreshControl_Click(object sender, RoutedEventArgs e)
    {
        if (manualRefreshing || !CanConnect()) return;
        var lifetime = controlLifetime;
        manualRefreshing = true;
        ControlFeedback.Text = "";
        UpdateControlUi();
        try { await RefreshControlAsync(manual: true); }
        finally
        {
            if (ReferenceEquals(lifetime, controlLifetime)) { manualRefreshing = false; UpdateControlUi(); }
        }
    }
    private void QueueFeatures()
    {
        if (!connected) { UpdateControlUi(); return; }
        pendingFeatures = featureControls.ToDictionary(pair => pair.Key, pair =>
            new FeatureState(pair.Value.Toggle.IsOn, multipliers[Math.Clamp((int)Math.Round(pair.Value.Multiplier.Value), 0, 3)]));
        editRevision++;
        featureDebounce.Stop(); featureDebounce.Start();
    }
    private async Task SendPendingFeaturesAsync()
    {
        if (draggingSliders.Count > 0 || sendingFeatures) return;
        featureDebounce.Stop();
        if (pendingFeatures == null) return;
        var revision = editRevision;
        var lifetime = controlLifetime;
        var desired = new Dictionary<string, FeatureState>(pendingFeatures);
        sendingFeatures = true;
        try { await RefreshControlAsync(desired); }
        finally { sendingFeatures = false; }
        if (!ReferenceEquals(lifetime, controlLifetime)) return;
        if (!connected)
        {
            pendingFeatures = null;
            featureDebounce.Stop();
            UpdateControlUi();
        }
        else if (revision == editRevision)
        {
            pendingFeatures = null;
            UpdateControlUi();
        }
        else if (pendingFeatures != null && connected) featureDebounce.Start();
    }
    private async Task RefreshControlAsync(Dictionary<string, FeatureState>? desired = null, bool manual = false)
    {
        if (!manual && desired == null && (pendingFeatures != null || manualRefreshing)) return;
        var session = controlLifetime;
        if (session == null || session.IsCancellationRequested || !pageActive || !controlVisible || target == null) return;
        var token = session.Token; string directory = target; bool entered = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (manual) deadline.CancelAfter(TimeSpan.FromSeconds(6));
        var requestToken = deadline.Token;
        try
        {
            if (desired != null || manual) { await controlGate.WaitAsync(requestToken); entered = true; }
            else entered = await controlGate.WaitAsync(0, token);
            if (!entered) return;
            if (!CanConnect()) { connected = false; return; }
            bool reconnect = !connected;
            if (desired != null) { commandPending = true; UpdateControlUi(); }
            var reply = await ModControlClient.SendFeaturesAsync(game.AppId, directory, desired, requestToken);
            if (reply.Error != null || !reply.Ready) throw new IOException(reply.Error ?? L.T("模组尚未就绪。"));
            if (desired == null && (reconnect || gameSession != reply.Session))
            {
                // Restore only launcher-session memory, never a prior process's persistent settings.
                var restore = pendingFeatures ?? ModSessionCache.Get(game.AppId, directory) ?? featureControls.Keys.ToDictionary(id => id, _ => new FeatureState());
                reply = await ModControlClient.SendFeaturesAsync(game.AppId, directory, restore, requestToken);
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
            if (!token.IsCancellationRequested) { connected = false; ControlFeedback.Text = manual ? L.T("连接失败，请确认游戏已启动且 Mod 已加载，再重试。") : desired == null ? "" : L.T("未收到确认，请重新连接核对。"); System.Diagnostics.Debug.WriteLine(ex); }
        }
        finally
        {
            if (entered) controlGate.Release();
            if (entered && ReferenceEquals(session, controlLifetime) && !token.IsCancellationRequested) { commandPending = false; UpdateControlUi(); }
        }
    }
    private void CopyLogs_Click(object sender, RoutedEventArgs e)
    {
        var content = new DataPackage();
        content.SetText(string.Join(Environment.NewLine, LogLines.Blocks.OfType<Paragraph>()
            .Select(p => string.Concat(p.Inlines.OfType<Run>().Select(run => run.Text)))));
        Clipboard.SetContent(content);
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
                var paragraph = new Paragraph();
                paragraph.Inlines.Add(new Run { Text = formatted.Text,
                    Foreground = new SolidColorBrush(line.TrimStart().StartsWith("[Warning", StringComparison.OrdinalIgnoreCase) ? Microsoft.UI.Colors.Goldenrod : formatted.Error ? Microsoft.UI.Colors.IndianRed : formatted.Resource ? Microsoft.UI.Colors.MediumSeaGreen : ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.LightSteelBlue : Microsoft.UI.Colors.SlateGray) });
                LogLines.Blocks.Add(paragraph);
            }
            while (LogLines.Blocks.Count > 500) LogLines.Blocks.RemoveAt(0);
            if (atEnd) LogScroll.ChangeView(null, double.MaxValue, null, true);
        }
        catch (IOException) { }
    }
}
