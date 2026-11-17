using System.Diagnostics;
using System.Text;

namespace QuickBoard.Core;

/// <summary>
/// 启动摆位探针：真实启动一个 GUI 程序，走完整的 Launcher.Launch → 等窗口 → 摆位链路，
/// 输出摆位结论与窗口最终矩形。用于验证「UseShellExecute 下拿不到 MainWindowHandle」的兜底是否生效。
/// 用法：--launchprobe &lt;report.txt&gt; [目标exe，默认 notepad.exe]
/// 说明：需要一个能真正创建窗口的桌面会话；在会被回收子进程的环境里跑没有意义。
/// </summary>
public static class LaunchProbe
{
    public static void Run(string reportPath, string? target = null)
    {
        var log = new StringBuilder();
        string exe = string.IsNullOrWhiteSpace(target) ? @"C:\Windows\System32\notepad.exe" : target;

        var before = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe))
            .Select(p => p.Id).ToHashSet();

        var entry = new AppEntry
        {
            Name = Path.GetFileNameWithoutExtension(exe),
            Target = exe,
            WindowPlacement = "LeftHalf",
            WindowMoveDelay = 300
        };

        Launcher.LastPlacementNote = "";   // 清空上一次结论，避免误读

        var result = Launcher.Launch(entry);
        log.AppendLine(result.Ok ? $"OK  {entry.Name} 已启动" : $"FAIL 启动失败：{result.Error}");

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 12000 && Launcher.LastPlacementNote.Length == 0)
            Thread.Sleep(150);

        log.AppendLine(Launcher.LastPlacementNote.Length > 0
            ? $"     摆位结论: {Launcher.LastPlacementNote}"
            : "FAIL 12 秒内没有拿到摆位结论");

        // 找到探测期间新出现的进程，报告其窗口矩形（证明真的移动过）
        try
        {
            var after = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe))
                .Where(p => !before.Contains(p.Id)).ToList();

            log.AppendLine($"     新建进程: {after.Count} 个");
            foreach (var p in after)
            {
                try
                {
                    p.Refresh();
                    var h = p.MainWindowHandle;
                    string rect = "";
                    if (h != IntPtr.Zero && GetWindowRect(h, out var r))
                        rect = $" 窗口=({r.Left},{r.Top})-({r.Right},{r.Bottom}) {r.Right - r.Left}×{r.Bottom - r.Top}";
                    log.AppendLine($"     pid={p.Id} hwnd=0x{h.ToInt64():X}{rect}");
                }
                catch (Exception ex)
                {
                    log.AppendLine($"     pid={p.Id} 读取窗口失败: {ex.Message}");
                }

                try { p.Kill(); } catch { /* 可能已退出 */ }
            }
        }
        catch (Exception ex)
        {
            log.AppendLine("     收尾失败: " + ex.Message);
        }

        File.WriteAllText(reportPath, log.ToString());
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
}
