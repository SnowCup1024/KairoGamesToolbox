namespace KairosoftGameToolbox.Services;

/// <summary>进程生命周期内持有固定命名互斥量；名称不随版本、目录或 EXE 文件名改变。</summary>
public sealed class LauncherInstanceLease : IDisposable
{
    public const string InstanceName = @"Local\SnowCup1024.KairoGamesToolbox.Launcher";
    private readonly Mutex mutex;
    private bool disposed;

    private LauncherInstanceLease(Mutex mutex) => this.mutex = mutex;

    // 获取和释放必须在同一线程；应用在 UI 线程获取并持有至进程结束。
    public static LauncherInstanceLease? TryAcquire(string name = InstanceName)
    {
        var mutex = new Mutex(false, name);
        try
        {
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (acquired) return new LauncherInstanceLease(mutex);
            mutex.Dispose();
            return null;
        }
        catch { mutex.Dispose(); throw; }
    }

    public void Dispose()
    {
        if (disposed) return;
        mutex.ReleaseMutex();
        mutex.Dispose();
        disposed = true;
    }
}
