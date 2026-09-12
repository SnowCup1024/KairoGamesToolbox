using KairoMods.DreamTownIsland;

internal static class ObservationChecks
{
    public static void Run(Action<string, bool> check)
    {
        long now = 0;
        int formatted = 0;
        var lines = new List<string>();
        var warnings = new List<string>();
        var log = new ObservationLog(() => now);
        for (int i = 0; i < 1000; i++) log.Record("money", () => { formatted++; return "transaction"; });
        for (int i = 0; i < 3; i++) { now += 100; log.Flush(lines.Add, warnings.Add); }
        check("刷屏时限制详细日志与字符串构造开销", formatted == 8 && lines.Count == 8);
        now = 10000;
        log.Flush(lines.Add, warnings.Add);
        check("限频保留总调用次数与隐藏数量", lines.Any(s => s.Contains("windowCalls=1000") && s.Contains("hidden=992") && s.Contains("total=1000")));
        log.Record("money", () => "next-window");
        now += 100;
        log.Flush(lines.Add, warnings.Add);
        check("下个窗口恢复样本而非永久关闭探针", lines.Contains("next-window"));
        var saturated = new ObservationLog(() => now);
        for (int key = 0; key < 100; key++)
            for (int i = 0; i < 8; i++) saturated.Record("event" + key, () => "detail");
        now += 10000;
        saturated.Flush(lines.Add, warnings.Add);
        check("队列过载有明确丢弃诊断", warnings.Any(s => s.Contains("buffer limit") && !s.EndsWith("=0")));
    }
}
