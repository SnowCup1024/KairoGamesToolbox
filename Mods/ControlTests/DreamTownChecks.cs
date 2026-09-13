using KairoMods.DreamTownIsland;
using KairoMods.Protocol;
using KairosoftGameToolbox.Services;

internal static class DreamTownChecks
{
    public static async Task RunAsync(Action<string, bool> check)
    {
        var state = new ControlState();
        check("都市岛四项默认关闭，四种点数共享一个功能且拒绝未知 ID", state.Snapshot().Count == 4 && state.Snapshot().Values.All(s => !s.Enabled)
            && Enumerable.Range(0, 4).All(id => ControlState.PointFeature(id) == "pointReverse") && ControlState.PointFeature(4) == null && ControlState.PointFeature(-1) == null);
        foreach (var multiplier in new[] { 0, 1, 50 })
        {
            var on = new FeatureState(true, multiplier);
            check($"都市岛 {multiplier}x 使用实际扣除而非请求值", 94 + ControlState.Refund(100, 94, -10, on, 9999) == 100 + 6 * multiplier);
            check($"都市岛 {multiplier}x 建筑最后一份库存反加", ControlState.Refund(1, 0, -1, on, 9999) == 1 + multiplier);
            check($"都市岛 {multiplier}x 收入、未扣除与关闭不补回", ControlState.Refund(100, 110, 10, on, 9999) == 0
                && ControlState.Refund(100, 100, -10, on, 9999) == 0 && ControlState.Refund(100, 90, -10, new(false, multiplier), 9999) == 0);
        }
        check("游戏数值上限、非法倍率、负库存与 long 极值受保护", ControlState.Refund(9998, 9997, -1, new(true, 50), 9999) == 0
            && ControlState.Refund(10, 9, -1, new(true, 3), 9999) == 0
            && ControlState.Refund(-1, -2, -1, new(true), 9999) == 0
            && ControlState.Refund(long.MaxValue, long.MinValue, long.MinValue, new(true, 50), long.MaxValue) == 0);
        var desired = state.Snapshot();
        desired["pointReverse"] = new(true, 1);
        desired["buildingReverse"] = new(true, 50);
        check("统一点数倍率不会开启道具或金钱", state.Configure(desired) == null && Enumerable.Range(0, 4).All(id => state.Get(ControlState.PointFeature(id)!) == new FeatureState(true, 1))
            && !state.Get("itemReverse").Enabled && !state.Get("moneyReverse").Enabled);
        var bad = new Dictionary<string, FeatureState>(desired) { ["itemReverse"] = new(true, 3) };
        check("拒绝不完整、未知和非法设置且原状态不变", state.Configure(bad) != null && state.Configure(new()) != null
            && state.Get("buildingReverse") == new FeatureState(true, 50) && !state.Get("itemReverse").Enabled);
        check("不接受旧四种点数设置混入新功能集合", state.Configure(new(desired) { ["foodReverse"] = new(true) }) != null);
        foreach (var multiplier in new[] { 0, 1, 50 })
            check($"道具消耗两份在 {multiplier}x 时只按实际扣除补回", 1 + ControlState.Refund(3, 1, -2, new(true, multiplier), 9999) == 3 + 2 * multiplier);
        check("新插件进程状态重新默认关闭", new ControlState().Snapshot().Values.All(s => !s.Enabled));

        var definition = BundledModService.ForGame(2488340)!;
        check("启动器功能集合与插件完全一致", definition.Features.Select(f => f.Id).Order().SequenceEqual(ControlState.FeatureIds.Order()));
        check("四项名称、描述和日志名均覆盖四语言", definition.Features.All(f => new[] { "zh-CN", "zh-TW", "en", "ja" }
            .All(l => !string.IsNullOrWhiteSpace(f.Names.GetValueOrDefault(l)) && !string.IsNullOrWhiteSpace(f.Descriptions.GetValueOrDefault(l))
                && !string.IsNullOrWhiteSpace(f.LogNames?.GetValueOrDefault(l)))));
        using var payload = typeof(BundledModService).Assembly.GetManifestResourceStream("ModPayload.2488340." + definition.Files.Single().Path)!;
        check("四项控制载荷匹配内嵌 SHA256", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)) == definition.Files.Single().Sha256);
        var sample = "ResourceResult | time=2026-09-12T18:00:00+08:00 | resource=pointReverse | before=10 | after=15 | delta=5";
        check("验收开发模式保留原文，正常模式按游戏规则翻译", ModLogReader.Format(sample, definition with { Development = true }, "zh-CN").Text == sample
            && ModLogReader.Format(sample, definition with { Development = false }, "zh-CN").Text.Contains("增加点数 5"));

        string directory = Path.Combine(Path.GetTempPath(), "DreamTownControl-" + Guid.NewGuid().ToString("N"));
        using var server = new GameControlServer(directory);
        server.Start(ex => check("都市岛管道监听异常: " + ex.Message, false));
        var session = Guid.NewGuid().ToString("N");
        int applied = 0;
        var live = new ControlState();
        ModControlResponse Handle(ModControlRequest request)
        {
            string? error = request.Protocol != 2 ? "Invalid protocol" : request.Action == "set" ? live.Configure(request.Features)
                : request.Action == "status" ? null : "Unknown action";
            if (request.Action == "set" && error == null) applied++;
            return new(2, 2488340, directory, session, true, live.Snapshot(), error);
        }
        async Task<ModControlResponse> Pump(Task<ModControlResponse> task)
        {
            while (!task.IsCompleted) { server.Pump(Handle); await Task.Delay(10); }
            return await task;
        }
        var initial = await Pump(ModControlClient.SendFeaturesAsync(2488340, directory));
        check("真实管道返回四项默认关闭与正确身份", initial.AppId == 2488340 && initial.Features.Count == 4 && initial.Features.Values.All(s => !s.Enabled));
        var pending = ModControlClient.SendFeaturesAsync(2488340, directory, desired);
        await Task.Delay(100);
        check("后台收到设置不会自行修改游戏状态", applied == 0);
        var reply = await Pump(pending);
        check("主线程泵确认设置并保留会话", applied == 1 && reply.Session == initial.Session && reply.Features["pointReverse"] == new FeatureState(true, 1));
        var expired = ModControlClient.SendFeaturesAsync(2488340, directory, new ControlState().Snapshot());
        try { await expired; } catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
        server.Pump(Handle);
        check("游戏停顿后过期指令不会补执行", applied == 1 && live.Get("pointReverse").Enabled);
    }
}
