using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuickBoard.Core;

/// <summary>
/// 布局探针：把窗口按指定尺寸铺开，量出真实渲染几何（Viewbox 缩放、键盘网格、首末磁贴）。
/// 用于在没有可靠截图手段时验证「三排对齐」与「等比缩放不溢出」。
/// </summary>
public static class LayoutProbe
{
    public static void Run(ConfigStore store, BoardConfig config, string reportPath, double width = 1180, double height = 660)
    {
        var log = new StringBuilder();
        log.AppendLine($"请求窗口尺寸: {width}x{height}");

        var window = new MainWindow(store, config)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -4000,
            Top = -4000,
            Width = width,
            Height = height,
            ShowInTaskbar = false,
            SuppressPersist = true
        };

        try
        {
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

            log.AppendLine($"实际窗口尺寸: {window.ActualWidth}x{window.ActualHeight}");

            var scaleBox = window.ScaleBox;
            var scaled = (FrameworkElement)scaleBox.Child;
            double scaleX = scaled.ActualWidth > 0 ? scaleBox.ActualWidth / scaled.ActualWidth : 0;
            double scaleY = scaled.ActualHeight > 0 ? scaleBox.ActualHeight / scaled.ActualHeight : 0;
            log.AppendLine($"Viewbox: {scaleBox.ActualWidth:0.#}x{scaleBox.ActualHeight:0.#} 内容 {scaled.ActualWidth:0.#}x{scaled.ActualHeight:0.#} 缩放 {scaleX:0.0000}/{scaleY:0.0000}");

            var grid = window.SlotGrid;
            log.AppendLine($"键盘网格: {grid.ActualWidth:0.#}x{grid.ActualHeight:0.#} 单元 {grid.Items.Count} 个");

            for (int row = 0; row < SlotKeys.Rows; row++)
            {
                int first = row * SlotKeys.Columns;
                int last = first + SlotKeys.Columns - 1;
                var a = BoundsOf(grid, first);
                var b = BoundsOf(grid, last);
                log.AppendLine($"  第 {row + 1} 排: 首格 x={a.Left:0.#} 末格右缘 x={b.Right:0.#} 格高 {a.Height:0.#}");
            }

            var lastBounds = BoundsOf(grid, grid.Items.Count - 1);
            log.AppendLine($"溢出检查: 网格右缘 {grid.ActualWidth:0.#} / 末格右缘 {lastBounds.Right:0.#} -> " +
                           (lastBounds.Right > grid.ActualWidth + 0.5 ? "溢出" : "未溢出"));

            // 逐格高度：磁贴尺寸是否真的统一（有内容的格子）
            var heights = new List<string>();
            for (int i = 0; i < Math.Min(12, grid.Items.Count); i++)
            {
                var container = grid.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                var cell = container is null ? null : FindButton(container);
                if (cell is null) continue;
                heights.Add($"{i + 1}:{cell.ActualHeight:0.#}");
            }
            log.AppendLine("有内容格子的按钮高度: " + string.Join("  ", heights));

            var fav = window.FavoritesBar;
            log.AppendLine($"收藏栏: 可见={fav.Visibility} 条目={fav.Items.Count} 尺寸 {fav.ActualWidth:0.#}x{fav.ActualHeight:0.#}");

            var groupBar = window.GroupBar;
            log.AppendLine($"分组栏: 条目={groupBar.Items.Count} 尺寸 {groupBar.ActualWidth:0.#}x{groupBar.ActualHeight:0.#}");
            log.AppendLine($"当前分组: {window.CurrentGroupName}");

            // 三排是否真的分三行（y 递增、行内 y 相同）
            var rowTops = new List<double>();
            for (int row = 0; row < SlotKeys.Rows; row++)
            {
                rowTops.Add(BoundsOf(grid, row * SlotKeys.Columns).Top);
            }
            log.AppendLine($"三排 y 坐标: {string.Join(" / ", rowTops.Select(v => v.ToString("0.#")))}");
            bool aligned = rowTops.Zip(rowTops.Skip(1), (a, b) => b > a + 1).All(x => x);
            log.AppendLine($"三排纵向递增: {(aligned ? "是" : "否")}");

            // 名称控件宽度必须统一：宽度若跟着文字长度走，短名称会被二次折行、长名称却不折
            var nameWidths = new List<double>();
            for (int i = 0; i < grid.Items.Count; i++)
            {
                var container = grid.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container is null) continue;
                var name = FindNameControl(container);
                if (name is null) continue;
                nameWidths.Add(Math.Round(name.ActualWidth, 1));
            }

            var distinct = nameWidths.Distinct().OrderBy(v => v).ToList();
            if (distinct.Count == 0)
                log.AppendLine("名称控件宽度: 未找到名称控件");
            else if (distinct.Count == 1)
                log.AppendLine($"名称控件宽度: 统一 {distinct[0]:0.#}（{nameWidths.Count} 个）");
            else
                log.AppendLine($"名称控件宽度: 不一致 {string.Join(" / ", distinct.Select(v => v.ToString("0.#")))}");
        }
        catch (Exception ex)
        {
            log.AppendLine("探针异常: " + ex);
        }
        finally
        {
            window.Hide();
            window.Close();
        }

        File.WriteAllText(reportPath, log.ToString());
    }

    private static OutlinedTextBlock? FindNameControl(DependencyObject root)
    {
        if (root is OutlinedTextBlock text) return text;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = FindNameControl(VisualTreeHelper.GetChild(root, i));
            if (found is not null) return found;
        }

        return null;
    }

    private static System.Windows.Controls.Button? FindButton(DependencyObject root)
    {
        if (root is System.Windows.Controls.Button button && button.Visibility == Visibility.Visible)
            return button;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = FindButton(VisualTreeHelper.GetChild(root, i));
            if (found is not null) return found;
        }

        return null;
    }

    private static Rect BoundsOf(ItemsControl items, int index)
    {
        if (items.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container)
            return Rect.Empty;

        try
        {
            var transform = container.TransformToAncestor(items);
            var origin = transform.Transform(new Point(0, 0));
            return new Rect(origin, new Size(container.ActualWidth, container.ActualHeight));
        }
        catch
        {
            return Rect.Empty;
        }
    }
}
