using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickBoard.Core;

/// <summary>
/// 内存构成诊断：以真实主窗口运行 3 秒（图标异步加载完成）后，拆出
/// 私有内存 / 工作集 / 托管堆 / 已加载程序集 / 图标像素缓存 / 界面图像资源 各自的占用。
/// 只读诊断，SuppressPersist 不写配置。
/// </summary>
public static class MemProbe
{
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

    public static void Run(ConfigStore store, BoardConfig config, string outputPath, string mode = "")
    {
        Window window;
        if (mode == "bare")
        {
            // 实验：完全不构造 MainWindow，只开一个同等尺寸的透明窗口
            var bare = new Window
            {
                Width = 1715,
                Height = 752,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ShowInTaskbar = false,
                Left = -4000,
                Top = -4000,
                Content = new TextBlock { Text = "bare" }
            };
            window = bare;
        }
        else
        {
            window = new MainWindow(store, config)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000,
                Top = -4000,
                ShowInTaskbar = false,
                SuppressPersist = true
            };
        }

        // mode=ctor：构造完不显示，用来分辨"构造开销"与"显示/合成开销"
        if (mode != "ctor") window.Show();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        int ticks = 0;
        var frame = new System.Windows.Threading.DispatcherFrame();
        timer.Tick += (s, e) =>
        {
            if (++ticks < 8) return;
            timer.Stop();
            try
            {
                if (mode == "slim")
                {
                    // 实验：把主界面整体换成一个 TextBlock。
                    // 若内存大幅回落 → 开销在界面/模板/特效；若几乎不动 → 开销在代码与框架层。
                    window.Content = new TextBlock { Text = "slim" };
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
                }

                var log = BuildReport(window, mode);
                File.WriteAllText(outputPath, log);
                Console.WriteLine(log);
            }
            catch (Exception ex)
            {
                File.WriteAllText(outputPath, "memprobe error: " + ex);
            }
            finally
            {
                window.Close();
                frame.Continue = false;
            }
        };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static string BuildReport(Window window, string mode)
    {
        var log = new StringBuilder();
        var proc = Process.GetCurrentProcess();

        var title = mode switch
        {
            "slim" => "=== QuickBoard 内存诊断（精简界面：主窗口内容只剩一个 TextBlock）===",
            "bare" => "=== QuickBoard 内存诊断（不构造 MainWindow：同等尺寸透明窗口 + TextBlock）===",
            "ctor" => "=== QuickBoard 内存诊断（只构造 MainWindow，未 Show）===",
            _ => "=== QuickBoard 内存诊断 ==="
        };
        log.AppendLine(title);
        log.AppendLine($"工作集 WS      : {proc.WorkingSet64 / 1048576.0:F1} MB");
        log.AppendLine($"私有内存 Private: {proc.PrivateMemorySize64 / 1048576.0:F1} MB");
        log.AppendLine($"托管堆 Managed  : {GC.GetTotalMemory(false) / 1048576.0:F1} MB");
        log.AppendLine($"线程 / 句柄     : {proc.Threads.Count} / {proc.HandleCount}");
        log.AppendLine($"GDI / USER 对象 : {GetGuiResources(proc.Handle, 0)} / {GetGuiResources(proc.Handle, 1)}");

        // ---- 分阶段归因：每步移除一项，看私有内存下降多少 ----
        log.AppendLine();
        log.AppendLine("--- 分阶段归因（每步移除一项后回收再测）---");
        double baseline = Settle(window, proc);
        log.AppendLine($"① 完整界面                     : {baseline,7:F1} MB");

        int shadowCount = 0;
        Walk(window, node =>
        {
            if (node is UIElement { Effect: not null } el) { el.Effect = null; shadowCount++; }
            return true;
        });
        double afterShadow = Settle(window, proc);
        log.AppendLine($"② 去掉投影特效（{shadowCount} 处）      : {afterShadow,7:F1} MB  （-{baseline - afterShadow:F1}）");

        int cleared = 0;
        var clearedBitmaps = new List<BitmapSource>();
        Walk(window, node =>
        {
            if (node is Image { Source: BitmapSource bmp } img)
            {
                if (!clearedBitmaps.Contains(bmp)) clearedBitmaps.Add(bmp);
                img.Source = null;
                cleared++;
            }
            return true;
        });
        IconService.ClearMemoryCache();
        double afterIcons = Settle(window, proc);
        log.AppendLine($"③ 释放 {cleared} 个图标（含像素缓存）  : {afterIcons,7:F1} MB  （-{afterShadow - afterIcons:F1}）");

        double afterPhoto = afterIcons;
        if (window.FindName("PhotoBrush") is Border photo && photo.Background is not null)
        {
            var brush = photo.Background;
            photo.Background = System.Windows.Media.Brushes.Transparent;
            if (window.FindName("PhotoLayer") is Grid layer) layer.Visibility = Visibility.Collapsed;
            afterPhoto = Settle(window, proc);
            log.AppendLine($"④ 释放背景图 ImageBrush        : {afterPhoto,7:F1} MB  （-{afterIcons - afterPhoto:F1}）");
            GC.KeepAlive(brush);
        }
        else
        {
            log.AppendLine("④ 未设置背景图，跳过");
        }

        double afterHide = afterPhoto;
        window.Hide();
        afterHide = Settle(window, proc);
        log.AppendLine($"⑤ 隐藏窗口（释放合成面）        : {afterHide,7:F1} MB  （-{afterPhoto - afterHide:F1}）");

        // ---- 明细 ----
        var asms = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .Select(a =>
            {
                long size = 0;
                try { if (a.Location.Length > 0) size = new FileInfo(a.Location).Length; } catch { }
                return new { a.GetName().Name, Size = size };
            })
            .OrderByDescending(x => x.Size)
            .ToList();

        log.AppendLine();
        log.AppendLine($"已加载程序集: {asms.Count} 个，磁盘镜像合计 {asms.Sum(x => x.Size) / 1048576.0:F1} MB");
        foreach (var a in asms.Take(12))
            log.AppendLine($"  {a.Size / 1024.0,8:F0} KB  {a.Name}");

        var cache = IconService.DescribeCache();
        log.AppendLine();
        log.AppendLine($"图标内存缓存（清除前）: 见阶段③；尺寸分布 {cache.Sizes}");

        return log.ToString();
    }

    /// <summary>强制回收 + 跑完布局/合成，再读私有内存（接近稳态值）。</summary>
    private static double Settle(Window window, Process proc)
    {
        for (int i = 0; i < 3; i++)
        {
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        GC.Collect();
        // 让 DWM/合成线程把释放的图面还回去
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        proc.Refresh();
        return proc.PrivateMemorySize64 / 1048576.0;
    }

    private static void Walk(DependencyObject root, Func<DependencyObject, bool> visit)
    {
        if (!visit(root)) return;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
            Walk(VisualTreeHelper.GetChild(root, i), visit);
    }
}
