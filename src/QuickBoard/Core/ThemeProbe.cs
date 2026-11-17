using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickBoard.Core;

/// <summary>
/// 主题探针：在同一真实窗口上依次应用深色/浅色，导出截图并记录关键画刷颜色，
/// 证明主题切换真的改写了运行时资源（不是只改了配置项）。
/// </summary>
public static class ThemeProbe
{
    public static void Run(ConfigStore store, BoardConfig config, string dir, string reportPath)
    {
        Directory.CreateDirectory(dir);
        var log = new StringBuilder();

        var window = new MainWindow(store, config)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -5000,
            Top = -5000,
            Width = 1180,
            Height = 660,
            ShowInTaskbar = false,
            SuppressPersist = true
        };

        try
        {
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

            foreach (var (name, kind, scale) in new[]
            {
                ("dark_standard", ThemeKinds.Dark, IconScales.Standard),
                ("light_large", ThemeKinds.Light, IconScales.Large),
                ("dark_compact", ThemeKinds.Dark, IconScales.Compact)
            })
            {
                Appearance.Apply(0.96, kind, scale);
                window.UpdateLayout();

                var panel = Application.Current.Resources["PanelBrush"] as SolidColorBrush;
                var text = Application.Current.Resources["TextBrush"] as SolidColorBrush;
                var tile = Application.Current.Resources["TileIconSize"] as double?;
                log.AppendLine($"{name}: panel={panel?.Color} text={text?.Color} tileIcon={tile}");

                Capture(window, Path.Combine(dir, name + ".png"), log);
            }
        }
        finally
        {
            window.Hide();
            window.Close();
        }

        File.WriteAllText(reportPath, log.ToString());
    }

    private static void Capture(Window window, string path, StringBuilder log)
    {
        int width = (int)Math.Ceiling(window.ActualWidth);
        int height = (int)Math.Ceiling(window.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        log.AppendLine($"  -> {Path.GetFileName(path)} {width}x{height}");
    }
}
