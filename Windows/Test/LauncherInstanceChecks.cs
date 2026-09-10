using KairosoftGameToolbox.Services;

static class LauncherInstanceChecks
{
    public static void Run(Action<string, bool> check)
    {
        var name = @"Local\KairoInstanceTest-" + Guid.NewGuid();
        using (var first = LauncherInstanceLease.TryAcquire(name))
        {
            check("首个启动器可以获得实例锁", first != null);
            bool blocked = false;
            var contender = new Thread(() =>
            {
                using var second = LauncherInstanceLease.TryAcquire(name);
                blocked = second == null;
            });
            contender.Start();
            contender.Join();
            check("其他启动线程不能获得已占用的实例锁", blocked);
        }
        using (var reopened = LauncherInstanceLease.TryAcquire(name))
            check("退出释放后可以重新启动", reopened != null);

        Mutex? abandoned = null;
        var thread = new Thread(() => { abandoned = new Mutex(false, name); abandoned.WaitOne(); });
        thread.Start();
        thread.Join();
        try
        {
            using var recovered = LauncherInstanceLease.TryAcquire(name);
            check("持锁线程异常退出后可恢复启动", recovered != null);
        }
        finally { abandoned?.Dispose(); }
    }
}
