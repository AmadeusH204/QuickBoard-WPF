using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace QuickBoard.Core;

/// <summary>
/// 菜单探针：离屏打开磁贴右键菜单（有内容 / 空键位 / 分组胶囊三种），输出每项 IsEnabled 并渲染 PNG；
/// 程序化实例化 ToolTip（旧版本 StaticResource 前向引用的崩溃点）；
/// 在空键位上触发「编辑…」，验证编辑对话框能打开。
/// </summary>
public static class MenuProbe
{
    public static void Run(ConfigStore store, BoardConfig config, string outputDirectory, string reportPath)
    {
        Directory.CreateDirectory(outputDirectory);
        var log = new StringBuilder();

        var window = new MainWindow(store, config)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -6000,
            Top = -6000,
            ShowInTaskbar = false,
            SuppressPersist = true
        };

        try
        {
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);

            Button? filled = null, empty = null, pill = null;
            for (int i = 0; i < window.SlotGrid.Items.Count && (filled is null || empty is null); i++)
            {
                if (window.SlotGrid.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container) continue;
                var button = FindButton(container);
                if (button is null) continue;
                if (button.Tag is TileItem && filled is null) filled = button;
                if (button.Tag is null && empty is null) empty = button;
            }
            for (int i = 0; i < window.GroupBar.Items.Count && pill is null; i++)
            {
                if (window.GroupBar.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container)
                    pill = FindButton(container);
            }

            log.AppendLine(filled is null ? "FAIL 未找到有内容磁贴" : "OK  找到有内容磁贴");
            log.AppendLine(empty is null ? "FAIL 未找到空键位磁贴" : "OK  找到空键位磁贴");
            log.AppendLine(pill is null ? "FAIL 未找到分组胶囊" : "OK  找到分组胶囊");

            if (filled is not null) DumpMenu(window, filled, Path.Combine(outputDirectory, "menu_filled.png"), "有内容磁贴", log);
            if (empty is not null) DumpMenu(window, empty, Path.Combine(outputDirectory, "menu_empty.png"), "空键位", log);
            if (pill is not null) DumpMenu(window, pill, Path.Combine(outputDirectory, "menu_group.png"), "分组胶囊", log);

            // ---- ToolTip：旧版本打开即抛 XamlParseException ----
            try
            {
                var tip = new ToolTip { Content = "探针：ToolTip 资源解析测试" };
                tip.IsOpen = true;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                var bitmap = new RenderTargetBitmap(360, 48, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(tip);
                SavePng(bitmap, Path.Combine(outputDirectory, "tooltip.png"));
                tip.IsOpen = false;
                log.AppendLine("OK  ToolTip 打开并渲染成功（旧版本此处抛 XamlParseException）");
            }
            catch (Exception ex)
            {
                log.AppendLine($"FAIL ToolTip: {ex.GetType().Name} {ex.Message}");
            }

            // ---- 空键位 →「编辑…」应打开编辑对话框 ----
            if (empty is not null)
            {
                try
                {
                    bool dialogSeen = false;
                    var guard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                    guard.Tick += (_, _) =>
                    {
                        foreach (Window w in Application.Current.Windows)
                        {
                            if (w is Views.EditEntryDialog)
                            {
                                dialogSeen = true;
                                w.DialogResult = false;   // 取消，不落任何配置
                            }
                        }
                        guard.Stop();
                    };

                    var menu = empty.ContextMenu;
                    if (menu is not null)
                    {
                        menu.PlacementTarget = empty;
                        menu.IsOpen = true;
                        guard.Start();

                        var editItem = menu.Items.OfType<MenuItem>()
                            .FirstOrDefault(m => m.Header as string == "编辑…");
                        if (editItem is null)
                        {
                            log.AppendLine("FAIL 菜单里找不到「编辑…」");
                        }
                        else
                        {
                            editItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, editItem));
                            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                            log.AppendLine(dialogSeen
                                ? "OK  空键位「编辑…」打开了编辑条目对话框（随后自动取消，未写配置）"
                                : "FAIL 空键位「编辑…」没有打开编辑对话框");
                        }

                        if (menu.IsOpen) menu.IsOpen = false;
                    }
                }
                catch (Exception ex)
                {
                    log.AppendLine($"FAIL 空键位编辑测试: {ex.GetType().Name} {ex.Message}");
                }
            }

            // ---- 复制 → 空键位粘贴：菜单启用态 + 真实落位（数据语义另由 --selftest 覆盖）----
            if (filled is not null && empty is not null)
            {
                try
                {
                    int before = window.DebugEntryCount();

                    var copyMenu = filled.ContextMenu;
                    if (copyMenu is null) log.AppendLine("FAIL 有内容磁贴没有 ContextMenu");
                    else
                    {
                        copyMenu.PlacementTarget = filled;
                        copyMenu.IsOpen = true;
                        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

                        var copyItem = copyMenu.Items.OfType<MenuItem>()
                            .FirstOrDefault(m => m.Header as string == "复制");
                        if (copyItem is null || !copyItem.IsEnabled)
                        {
                            log.AppendLine("FAIL 有内容磁贴的「复制」不可用");
                        }
                        else
                        {
                            copyItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, copyItem));
                            copyMenu.IsOpen = false;
                            log.AppendLine(window.DebugHasClip ? "OK  「复制」后剪贴板非空" : "FAIL 「复制」后剪贴板仍为空");

                            var pasteMenu = empty.ContextMenu;
                            if (pasteMenu is null) log.AppendLine("FAIL 空键位没有 ContextMenu");
                            else
                            {
                                pasteMenu.PlacementTarget = empty;
                                pasteMenu.IsOpen = true;
                                window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

                                var pasteItem = pasteMenu.Items.OfType<MenuItem>()
                                    .FirstOrDefault(m => m.Header as string == "粘贴");
                                bool enabled = pasteItem?.IsEnabled == true;
                                log.AppendLine(enabled
                                    ? "OK  剪贴板非空时，空键位「粘贴」变为可用"
                                    : "FAIL 剪贴板非空时，空键位「粘贴」仍禁用");

                                if (pasteItem is not null && enabled)
                                {
                                    pasteItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, pasteItem));
                                    int after = window.DebugEntryCount();
                                    log.AppendLine(after == before + 1
                                        ? $"OK  粘贴落位成功：条目 {before} → {after}（SuppressPersist，未写盘）"
                                        : $"FAIL 粘贴后条目数应 +1，实际 {before} → {after}");
                                }

                                if (pasteMenu.IsOpen) pasteMenu.IsOpen = false;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.AppendLine($"FAIL 复制粘贴链路: {ex.GetType().Name} {ex.Message}");
                }
            }
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

    private static void DumpMenu(MainWindow window, Button target, string png, string label, StringBuilder log)
    {
        try
        {
            var menu = target.ContextMenu;
            if (menu is null)
            {
                log.AppendLine($"FAIL {label}: 按钮没有 ContextMenu");
                return;
            }

            menu.PlacementTarget = target;
            menu.IsOpen = true;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

            log.AppendLine($"---- {label} 右键菜单 ----");
            foreach (var item in menu.Items.OfType<MenuItem>())
                log.AppendLine($"    {item.Header}  IsEnabled={item.IsEnabled}");

            int width = (int)Math.Ceiling(menu.ActualWidth);
            int height = (int)Math.Ceiling(menu.ActualHeight);
            var bitmap = new RenderTargetBitmap(Math.Max(width, 1), Math.Max(height, 1), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(menu);
            SavePng(bitmap, png);
            log.AppendLine($"OK  {label} 菜单渲染 {width}x{height} -> {Path.GetFileName(png)}");

            menu.IsOpen = false;
        }
        catch (Exception ex)
        {
            log.AppendLine($"FAIL {label}: {ex.GetType().Name} {ex.Message}");
        }
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static Button? FindButton(DependencyObject root)
    {
        if (root is Button button && button.Visibility == Visibility.Visible)
            return button;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = FindButton(VisualTreeHelper.GetChild(root, i));
            if (found is not null) return found;
        }

        return null;
    }
}
