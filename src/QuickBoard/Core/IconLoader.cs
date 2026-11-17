using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;

namespace QuickBoard.Core;

/// <summary>
/// 图标串行加载器。
/// 原实现在 RebuildBoard 里给 42 个条目各发一个 <c>Task.Run</c>，42 个同步阻塞的 PNG 解码任务同时涌入
/// 线程池，线程池判定"线程都被阻塞"便持续注入新线程 —— 实测进程线程数从 29 涨到 59，
/// 每个线程都要保留/提交栈空间，纯属平台开销。
/// 这里改为一条常驻后台线程按队列顺序提取，线程池一个线程都不额外创建。
/// </summary>
public static class IconLoader
{
    private sealed class Job
    {
        public TileItem Item = null!;
        public string Target = "";
        public string IconPath = "";
    }

    private static readonly BlockingCollection<Job> Queue = new(new ConcurrentQueue<Job>());

    /// <summary>专属提取线程数：4 个足以在 1 秒内铺满 42 个图标，又不会像 Task.Run 那样把线程池打爆。</summary>
    private const int WorkerCount = 4;

    static IconLoader()
    {
        for (int i = 0; i < WorkerCount; i++)
        {
            var worker = new Thread(Loop)
            {
                IsBackground = true,
                Name = "QuickBoard.IconLoader#" + i
            };
            // Shell / COM 图标提取走 MTA 更稳
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        }
    }

    /// <summary>把条目的图标提取排进队列（同一条目重复调用会被去重，避免重复提取）。</summary>
    public static void Enqueue(TileItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Entry.Target)) return;
        if (!item.TryBeginIconLoad()) return;      // 已排过队 / 已有图标

        Queue.Add(new Job
        {
            Item = item,
            Target = item.Entry.Target,
            IconPath = item.Entry.IconPath
        });
    }

    private static void Loop()
    {
        foreach (var job in Queue.GetConsumingEnumerable())
        {
            ImageSource? icon;
            try
            {
                icon = IconService.Get(job.Target, job.IconPath);
            }
            catch
            {
                job.Item.CancelIconLoad();
                continue;
            }

            if (icon is null)
            {
                job.Item.CancelIconLoad();
                continue;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted) continue;

            // 图标的赋值必须在 UI 线程（会触发 PropertyChanged，绑定 Image.Source）
            dispatcher.BeginInvoke(new Action(() => job.Item.Icon = icon),
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
