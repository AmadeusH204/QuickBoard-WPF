using System.Windows;
using System.Windows.Media.Imaging;

namespace QuickBoard.Core;

/// <summary>把主界面（含壁纸、磁贴、名称描边）离屏渲染成 PNG，用于核对最终观感。</summary>
public static class BoardShot
{
    public static void Run(ConfigStore store, BoardConfig config, string outputPath)
    {
        var window = new MainWindow(store, config)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -4000,
            Top = -4000,
            ShowInTaskbar = false,
            SuppressPersist = true
        };

        window.Show();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

        // 等图标异步加载完成（IconService 后台加载后回 UI 线程），3 秒后渲染
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        int ticks = 0;
        var frame = new System.Windows.Threading.DispatcherFrame();
        timer.Tick += (s, e) =>
        {
            ticks++;
            if (ticks < 6) return;
            timer.Stop();
            try
            {
                window.UpdateLayout();

                // 增量刷新验证：切组前后 SlotCell 容器应复用（同一个对象），内容应更新
                var firstSlotBefore = window.SlotGrid.Items.Count > 0 ? window.SlotGrid.Items[0] : null;
                var nameBefore = (firstSlotBefore as SlotCell)?.Tile?.DisplayName;
                var favBefore = window.FavoritesBar.Items.Count > 0
                    ? ((FavSlot)window.FavoritesBar.Items[0]).Tile?.DisplayName
                    : null;
                window.DebugSelectGroup(1);
                window.UpdateLayout();
                var firstSlotAfter = window.SlotGrid.Items.Count > 0 ? window.SlotGrid.Items[0] : null;
                var nameAfter = (firstSlotAfter as SlotCell)?.Tile?.DisplayName;
                var favAfter = window.FavoritesBar.Items.Count > 0
                    ? ((FavSlot)window.FavoritesBar.Items[0]).Tile?.DisplayName
                    : null;
                Console.WriteLine(
                    $"INCREMENTAL reuse={ReferenceEquals(firstSlotBefore, firstSlotAfter)} " +
                    $"slot0=[{nameBefore ?? "-"}]=>[{nameAfter ?? "-"}] favF1=[{favBefore ?? "-"}]=>[{favAfter ?? "-"}]");

                // 回到第一个分组再渲染：那里名称最长（英文长名最多），是名称排版的压力样本
                window.DebugSelectGroup(0);
                window.UpdateLayout();

                int width = (int)Math.Ceiling(window.ActualWidth);
                int height = (int)Math.Ceiling(window.ActualHeight);
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(outputPath);
                encoder.Save(stream);
                Console.WriteLine($"saved {outputPath} {width}x{height}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("boardshot error: " + ex.Message);
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
}
