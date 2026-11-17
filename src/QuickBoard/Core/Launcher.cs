using System.Diagnostics;
using System.IO;

namespace QuickBoard.Core;

public sealed record LaunchResult(bool Ok, string? Error);

public static class Launcher
{
    /// <summary>
    /// 最近一次窗口摆位的结论（成功/失败原因）。供诊断与探针读取：
    /// 摆位是异步的，无法通过 LaunchResult 回传，只能在这里留痕。
    /// </summary>
    public static string LastPlacementNote { get; internal set; } = "";

    public static LaunchResult Launch(AppEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Target))
            return new LaunchResult(false, "该条目还没有设置目标");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = entry.Target,
                UseShellExecute = true
            };

            if (!string.IsNullOrWhiteSpace(entry.Arguments)) psi.Arguments = entry.Arguments;
            if (!string.IsNullOrWhiteSpace(entry.WorkingDirectory) && Directory.Exists(entry.WorkingDirectory))
                psi.WorkingDirectory = entry.WorkingDirectory;

            if (entry.RunAsAdmin) psi.Verb = "runas";

            psi.WindowStyle = entry.WindowState switch
            {
                "Maximized" => ProcessWindowStyle.Maximized,
                "Minimized" => ProcessWindowStyle.Minimized,
                _ => ProcessWindowStyle.Normal
            };

            // 启动前拍一张「已有顶层窗口」快照 + 当前前台窗口。
            // UseShellExecute 时 Process.MainWindowHandle 对 .lnk / 启动器类程序（QQ.exe 拉起另一个进程后
            // 自身退出 / Win11 记事本的打包存根）恒为 0，只能靠「多出来一个新窗口」来认领；
            // 用快照而不是裸 GetForegroundWindow，是为了避免用户此刻手动切窗口被误当成目标。
            var previousForeground = Native.GetForegroundWindow();
            var knownWindows = SnapshotWindows();

            var process = Process.Start(psi);

            if (!string.IsNullOrWhiteSpace(entry.WindowPlacement))
            {
                var placement = entry.WindowPlacement;
                var delay = entry.WindowMoveDelay;
                _ = ApplyPlacementAsync(process, previousForeground, knownWindows, placement, delay);
            }

            return new LaunchResult(true, null);
        }
        catch (Exception ex)
        {
            return new LaunchResult(false, ex.Message);
        }
    }

    /// <summary>
    /// 可选的「启动后摆放窗口」，对应原版的 Window Location / Window Move Delay。
    /// 全程异步：轮询用 Task.Delay，不占用线程池线程（旧实现 Thread.Sleep 最多阻塞 15 秒）。
    /// </summary>
    private static async Task ApplyPlacementAsync(
        Process? process, IntPtr previousForeground, HashSet<IntPtr> knownWindows, string placement, int delay)
    {
        try
        {
            await Task.Delay(Math.Clamp(delay, 100, 15000)).ConfigureAwait(false);

            var handle = await WaitForWindowAsync(process, previousForeground, knownWindows).ConfigureAwait(false);
            if (handle == IntPtr.Zero)
            {
                LastPlacementNote = "未等到目标窗口，摆位跳过";
                Debug.WriteLine(LastPlacementNote);
                return;
            }

            // 图形界面进程才等得到 InputIdle；Shell 转交的进程会抛异常，忽略即可
            try { process?.WaitForInputIdle(500); }
            catch (InvalidOperationException) { /* 非 GUI 进程或已退出 */ }

            bool moved = ApplyPlacement(handle, placement);
            LastPlacementNote = moved
                ? $"已摆位 {placement} (0x{handle.ToInt64():X})"
                : $"摆位未执行：未知方位「{placement}」";
        }
        catch (Exception ex)
        {
            LastPlacementNote = "窗口摆放失败: " + ex.Message;
            Debug.WriteLine(LastPlacementNote);
        }
    }

    /// <summary>
    /// 找目标窗口：
    /// 1) 先用 Process.MainWindowHandle（普通 exe 最可靠）；
    /// 2) 拿不到就与启动前的窗口快照比对，认领「新冒出来的顶层窗口」。
    ///    这一条覆盖 QQ.exe 这类「拉起别的进程后自身退出」和 Win11 记事本的打包存根 ——
    ///    它们的 MainWindowHandle 恒为 0，而原版正是靠裸 GetForegroundWindow 兜的；
    ///    我们用快照比对，额外避免了「用户此刻手动切窗口被误当成目标」。
    /// </summary>
    private static async Task<IntPtr> WaitForWindowAsync(
        Process? process, IntPtr previousForeground, HashSet<IntPtr> knownWindows)
    {
        const int tries = 50;   // 50 × 200ms ≈ 10 秒

        for (int i = 0; i < tries; i++)
        {
            if (process is not null)
            {
                try
                {
                    process.Refresh();
                    var main = process.MainWindowHandle;
                    if (main != IntPtr.Zero) return main;
                }
                catch (InvalidOperationException)
                {
                    // 进程已退出：子进程的窗口靠下面的快照比对认领
                }
            }

            var fresh = FindNewWindow(knownWindows, process, previousForeground);
            if (fresh != IntPtr.Zero) return fresh;

            await Task.Delay(200).ConfigureAwait(false);
        }

        return IntPtr.Zero;
    }

    /// <summary>启动前拍一张「已有可见且有标题的顶层窗口」快照。</summary>
    private static HashSet<IntPtr> SnapshotWindows()
    {
        var set = new HashSet<IntPtr>();
        Native.EnumWindows((h, _) =>
        {
            if (Native.IsWindowVisible(h) && Native.GetWindowTextLength(h) > 0) set.Add(h);
            return true;
        }, IntPtr.Zero);
        return set;
    }

    /// <summary>快照里没有、现在可见且有标题的顶层窗口；优先取属于本进程的那个。</summary>
    private static IntPtr FindNewWindow(HashSet<IntPtr> known, Process? process, IntPtr previousForeground)
    {
        IntPtr fallback = IntPtr.Zero;
        IntPtr owner = IntPtr.Zero;
        int processId = -1;

        if (process is not null)
        {
            try { processId = process.Id; }
            catch (InvalidOperationException) { /* 已退出，按无 pid 处理 */ }
        }

        Native.EnumWindows((h, _) =>
        {
            if (known.Contains(h) || h == previousForeground) return true;
            if (!Native.IsWindowVisible(h) || Native.GetWindowTextLength(h) == 0) return true;

            if (processId > 0)
            {
                Native.GetWindowThreadProcessId(h, out uint pid);
                if (pid == processId) { owner = h; return false; }
            }

            if (fallback == IntPtr.Zero) fallback = h;
            return true;
        }, IntPtr.Zero);

        return owner != IntPtr.Zero ? owner : fallback;
    }

    /// <summary>按方位摆放窗口；未知方位不干预。返回是否真的动过窗口。</summary>
    private static bool ApplyPlacement(IntPtr handle, string placement)
    {
        if (handle == IntPtr.Zero || !Native.IsWindow(handle)) return false;

        var kind = placement.Trim().ToLowerInvariant();

        if (kind is "maximize" or "maximized" or "最大化")
        {
            Native.ShowWindow(handle, Native.SW_MAXIMIZE);
            return true;
        }

        // 目标窗口所在的显示器（多屏时不能一律用主屏）
        var area = System.Windows.Forms.Screen.FromHandle(handle)?.WorkingArea
                   ?? System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
                   ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);

        int x = area.X, y = area.Y, w = area.Width, h = area.Height;

        switch (kind)
        {
            case "lefthalf":
                w = area.Width / 2;
                break;
            case "righthalf":
                x = area.X + area.Width / 2;
                w = area.Width / 2;
                break;
            case "center":
                w = (int)(area.Width * 0.75);
                h = (int)(area.Height * 0.8);
                x = area.X + (area.Width - w) / 2;
                y = area.Y + (area.Height - h) / 2;
                break;
            default:
                return false;
        }

        // 最大化/最小化的窗口 SetWindowPos 不生效，先还原
        if (Native.IsZoomed(handle) || Native.IsIconic(handle)) Native.ShowWindow(handle, Native.SW_RESTORE);

        // SWP_NOACTIVATE：摆位不抢焦点；配合 NOZORDER 保持原有前后关系
        return Native.SetWindowPos(handle, IntPtr.Zero, x, y, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }
}
