using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KairosoftGameToolbox.Services;

internal static class ExistingLauncherWindow
{
    // 返回 true 表示保留已有实例，调用者应退出。
    public static bool HandleExisting()
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
                        if (process.Id == current.Id || process.SessionId != current.SessionId || process.HasExited) continue;
                        var window = process.MainWindowHandle;
                        var file = process.MainModule?.FileName;
                        var info = file == null ? null : FileVersionInfo.GetVersionInfo(file);
                        // 只允许用户确认关闭已识别的本产品旧版；未知版本不会被终止。
                        if (info?.ProductName == "开罗游戏工具箱" && LauncherInstanceLease.IsOlderVersion(info.FileVersion, ReleaseInfo.Version))
                        {
                            var answer = MessageBox(IntPtr.Zero,
                                L.F("检测到旧版本 {0} 正在运行，当前版本为 {1}。是否关闭旧版本并启动当前版本？选择“否”将打开旧版本。", info.FileVersion!, ReleaseInfo.Version),
                                L.T("切换启动器版本"), 0x00000004 | 0x00000020 | 0x00000100 | 0x00010000);
                            if (answer == 6) // IDYES；用户明确同意关闭该旧版进程。
                            {
                                try
                                {
                                    if (!process.HasExited)
                                    {
                                        process.CloseMainWindow();
                                        if (!process.WaitForExit(3000))
                                        {
                                            process.Kill();
                                            if (!process.WaitForExit(3000)) throw new IOException("旧版本进程未退出。");
                                        }
                                    }
                                    continue;
                                }
                                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
                                {
                                    MessageBox(IntPtr.Zero, L.T("无法关闭旧版本，请手动退出后重新启动。"), L.T("切换启动器版本"), 0x00000010);
                                    TryActivate(process);
                                    return true;
                                }
                            }
                        }
                        Activate(window);
                        return true;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        if (TryActivate(process)) return true;
                    }
                }
            }
        }
        return false;
    }

    private static bool TryActivate(Process process)
    {
        try
        {
            if (process.HasExited) return false;
            Activate(process.MainWindowHandle);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static void Activate(IntPtr window)
    {
        if (window == IntPtr.Zero) return;
        if (IsIconic(window)) ShowWindow(window, 9);
        SetForegroundWindow(window);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
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
