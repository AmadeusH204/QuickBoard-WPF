using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickBoard.Core;

/// <summary>
/// 诊断 ComboBox 下拉项文字的实际前景色：确认是否被全局隐式 TextBlock 样式染成白色，
/// 导致浅色下拉底上白字看不清。修复前后各跑一次即可对比。
/// </summary>
public static class ComboProbe
{
    public static void Run(string reportPath, string? outputDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory)) Directory.CreateDirectory(outputDirectory);
        var log = new System.Text.StringBuilder();
        var store = new ConfigStore();
        var cfg = store.Load();

        foreach (var theme in new[] { ThemeKinds.Dark, ThemeKinds.Light })
        {
            Appearance.Apply(1.0, theme, IconScales.Standard);
            var dlg = new Views.SettingsDialog(cfg, store);
            dlg.WindowStartupLocation = WindowStartupLocation.Manual;
            dlg.Left = -6000; dlg.Top = -6000; dlg.ShowInTaskbar = false;
            dlg.Show();
            dlg.UpdateLayout();
            dlg.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

            log.AppendLine($"=== theme={(theme == ThemeKinds.Dark ? "Dark" : "Light")} ===");
            foreach (var name in new[] { "ThemeCombo", "IconScaleCombo", "FitCombo" })
            {
                var combo = dlg.FindName(name) as ComboBox;
                if (combo is null) { log.AppendLine($"{name}: NOT FOUND"); continue; }

                combo.ApplyTemplate();
                combo.IsDropDownOpen = true;
                dlg.UpdateLayout();
                dlg.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

                var texts = new System.Collections.Generic.List<string>();

                // 选中框文字（根本身也可能是 TextBlock，先单独取一次）
                var site = combo.Template.FindName("ContentSite", combo) as DependencyObject;
                if (site is TextBox tbRoot && !string.IsNullOrEmpty(tbRoot.Text))
                    texts.Add($"[box]'{tbRoot.Text}' fg={Fg(tbRoot.Foreground)}");
                else if (site is TextBlock tblRoot && !string.IsNullOrEmpty(tblRoot.Text))
                    texts.Add($"[box]'{tblRoot.Text}' fg={Fg(tblRoot.Foreground)}");
                if (site is not null) DumpTexts(site, "box", texts);

                // 下拉项文字（Popup 独立可视树）
                var popup = combo.Template.FindName("PART_Popup", combo) as System.Windows.Controls.Primitives.Popup;
                if (popup?.Child is not null)
                {
                    DumpTexts(popup.Child, "item", texts);
                    if (!string.IsNullOrWhiteSpace(outputDirectory))
                        RenderDropDown(combo, popup,
                            Path.Combine(outputDirectory, $"{(theme == ThemeKinds.Dark ? "dark" : "light")}_{name}.png"), log);
                }

                log.AppendLine($"{name}: " + string.Join(" | ", texts));
                combo.IsDropDownOpen = false;
            }
            dlg.Close();
        }

        File.WriteAllText(reportPath, log.ToString());
    }

    /// <summary>把整个 ComboBox（选中框 + 展开的下拉项）渲染成 PNG，直观核对白底深字效果。</summary>
    private static void RenderDropDown(ComboBox combo, System.Windows.Controls.Primitives.Popup popup,
        string path, System.Text.StringBuilder log)
    {
        try
        {
            var child = popup.Child as FrameworkElement;
            if (child is null) return;
            child.UpdateLayout();
            // Popup 是独立窗口，单独把下拉项部分渲染出来
            var drop = new RenderTargetBitmap((int)Math.Ceiling(child.ActualWidth),
                (int)Math.Ceiling(child.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            drop.Render(popup.Child);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(drop));
            using var fs = File.Create(path);
            enc.Save(fs);
            log.AppendLine($"PNG {Path.GetFileName(path)} {drop.PixelWidth}x{drop.PixelHeight}");
        }
        catch (Exception ex)
        {
            log.AppendLine($"PNG FAIL {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private static void DumpTexts(DependencyObject root, string scope, System.Collections.Generic.List<string> texts)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is TextBlock tb && !string.IsNullOrEmpty(tb.Text))
                texts.Add($"[{scope}]'{tb.Text}' fg={Fg(tb.Foreground)}");
            DumpTexts(c, scope, texts);
        }
    }

    private static string Fg(Brush? b) => b is SolidColorBrush s ? s.Color.ToString() : b?.ToString() ?? "null";
}
