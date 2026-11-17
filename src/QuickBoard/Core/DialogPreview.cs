using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickBoard.Core;

/// <summary>把对话框离屏渲染成 PNG，用于在没有可靠截图手段时核对界面渲染。</summary>
public static class DialogPreview
{
    public static void Run(BoardConfig config, ConfigStore store, string outputDirectory, string reportPath)
    {
        Directory.CreateDirectory(outputDirectory);
        var log = new System.Text.StringBuilder();

        var settings = new Views.SettingsDialog(config, store);
        Capture(settings, Path.Combine(outputDirectory, "settings.png"), log);
        log.AppendLine("settings state: " + settings.DebugState);

        // 把设置页滚到底部再截一张，验证「数据 / 诊断」区渲染
        var settingsBottom = new Views.SettingsDialog(config, store);
        CaptureScrolled(settingsBottom, Path.Combine(outputDirectory, "settings_bottom.png"), log);

        // 展开滚动区截一张完整长图，验证主题/尺寸下拉框等顶部控件
        var settingsFull = new Views.SettingsDialog(config, store);
        CaptureFull(settingsFull, Path.Combine(outputDirectory, "settings_full.png"), log);

        var groups = new Views.GroupEditorDialog(config);
        Capture(groups, Path.Combine(outputDirectory, "groups.png"), log);
        log.AppendLine("groups state: " + groups.DebugState);

        Capture(new Views.PromptDialog("重命名分组", "分组名称", "分组 1"), Path.Combine(outputDirectory, "prompt.png"), log);
        Capture(new Views.EditEntryDialog(config.Groups.FirstOrDefault(g => !g.IsFavorites)?.Entries.FirstOrDefault()
                                          ?? new AppEntry { Name = "示例", Target = @"C:\Windows\notepad.exe" }),
                Path.Combine(outputDirectory, "edit.png"), log);

        File.WriteAllText(reportPath, log.ToString());
    }

    private static void CaptureFull(Window window, string path, System.Text.StringBuilder log)
    {
        try
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -6000;
            window.Top = -6000;
            window.ShowInTaskbar = false;
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

            var viewer = window.FindName("SettingsScroller") as System.Windows.Controls.ScrollViewer;
            if (viewer is not null) { viewer.MaxHeight = 2000; window.UpdateLayout(); }

            int width = (int)Math.Ceiling(window.ActualWidth);
            int height = (int)Math.Ceiling(window.ActualHeight);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
            log.AppendLine($"OK  {Path.GetFileName(path)} {width}x{height} full");
        }
        catch (Exception ex)
        {
            log.AppendLine($"FAIL {Path.GetFileName(path)}: {ex.GetType().Name} {ex.Message}");
        }
        finally
        {
            window.Hide();
            window.Close();
        }
    }

    private static void CaptureScrolled(Window window, string path, System.Text.StringBuilder log)
    {
        try
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -6000;
            window.Top = -6000;
            window.ShowInTaskbar = false;
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

            var viewer = window.FindName("SettingsScroller") as System.Windows.Controls.ScrollViewer;
            if (viewer is not null)
            {
                viewer.ScrollToEnd();
                window.UpdateLayout();
            }

            int width = (int)Math.Ceiling(window.ActualWidth);
            int height = (int)Math.Ceiling(window.ActualHeight);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
            log.AppendLine($"OK  {Path.GetFileName(path)} {width}x{height} scrolled={viewer is not null}");
        }
        catch (Exception ex)
        {
            log.AppendLine($"FAIL {Path.GetFileName(path)}: {ex.GetType().Name} {ex.Message}");
        }
        finally
        {
            window.Hide();
            window.Close();
        }
    }

    private static void Capture(Window window, string path, System.Text.StringBuilder log)
    {
        try
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -6000;
            window.Top = -6000;
            window.ShowInTaskbar = false;
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

            int width = (int)Math.Ceiling(window.ActualWidth);
            int height = (int)Math.Ceiling(window.ActualHeight);
            if (width <= 0 || height <= 0) throw new InvalidOperationException("窗口尺寸为 0");

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);

            log.AppendLine($"OK  {Path.GetFileName(path)} {width}x{height}");
        }
        catch (Exception ex)
        {
            log.AppendLine($"FAIL {Path.GetFileName(path)}: {ex.GetType().Name} {ex.Message}");
        }
        finally
        {
            window.Hide();
            window.Close();
        }
    }
}
