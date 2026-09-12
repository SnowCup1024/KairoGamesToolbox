using System.Collections.Concurrent;

namespace KairoMods.DreamTownIsland;

internal sealed class ObservationLog
{
    private readonly ConcurrentQueue<string> queue = new();
    private readonly Dictionary<string, Frequency> frequencies = new();
    private readonly object gate = new();
    private readonly Func<long> clock;
    private long nextSummary;
    private long nextFlush;
    private int queued;
    private int dropped;
    private sealed class Frequency { public long Total; public int Window; public int Hidden; }

    public ObservationLog(Func<long>? clock = null)
    {
        this.clock = clock ?? (() => Environment.TickCount64);
        nextSummary = this.clock() + 10000;
    }

    public void Record(string key, Func<string> format)
    {
        lock (gate)
        {
            if (!frequencies.TryGetValue(key, out var value))
            {
                if (frequencies.Count >= 256) { Interlocked.Increment(ref dropped); return; }
                frequencies[key] = value = new();
            }
            value.Total++;
            if (++value.Window > 8) { value.Hidden++; return; }
        }
        Enqueue(format());
    }

    private void Enqueue(string text)
    {
        if (Interlocked.Increment(ref queued) > 500)
        {
            Interlocked.Decrement(ref queued);
            Interlocked.Increment(ref dropped);
            return;
        }
        queue.Enqueue(text);
    }

    public void Flush(Action<string> info, Action<string> warning)
    {
        var now = clock();
        if (now >= nextSummary)
        {
            nextSummary = now + 10000;
            lock (gate)
            {
                foreach (var (key, value) in frequencies.Where(p => p.Value.Window > 0))
                {
                    Enqueue($"ProbeFrequency | time={DateTimeOffset.Now:O} | key={key} | windowCalls={value.Window} | hidden={value.Hidden} | total={value.Total}");
                    value.Window = value.Hidden = 0;
                }
            }
            var lost = Interlocked.Exchange(ref dropped, 0);
            if (lost > 0) warning($"Probe log buffer limit | dropped={lost}");
        }
        if (now < nextFlush) return;
        nextFlush = now + 100;
        for (int i = 0; i < 4 && queue.TryDequeue(out var line); i++)
        {
            Interlocked.Decrement(ref queued);
            info(line);
        }
    }
}
