using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KairosoftGameToolbox.Services;

internal static class ExistingLauncherWindow
{
    // 兼容未实现命名互斥量的历史版本；不结束其他进程。
    public static bool Activate()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var name in new[] { "KairosoftGameToolbox", "KairoGamesToolbox", current.ProcessName }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        if (process.Id == current.Id || process.SessionId != current.SessionId) continue;
                        var window = process.MainWindowHandle;
                        if (window == IntPtr.Zero) continue;
                        if (IsIconic(window)) ShowWindow(window, 9); // SW_RESTORE
                        SetForegroundWindow(window);
                        return true;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // 枚举期间进程退出或不可访问，继续查找。
                    }
                }
            }
        }
        return false;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);
}
