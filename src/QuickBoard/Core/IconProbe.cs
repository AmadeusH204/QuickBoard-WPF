using System.Diagnostics;
using System.IO;
using System.Text;

namespace QuickBoard.Core;

/// <summary>
/// 图标诊断探针：逐个条目实际提取图标，报告来源、尺寸与耗时。
/// 仅诊断用，不参与正常运行。
/// </summary>
public static class IconProbe
{
    public static void Run(BoardConfig config, string reportPath)
    {
        var dir = Path.Combine(Path.GetDirectoryName(reportPath) ?? Path.GetTempPath(), "iconprobe-cache");
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        IconService.Initialize(dir);

        var sb = new StringBuilder();
        sb.AppendLine("group|name|source|exists|icon|ms|fallback");

        foreach (var group in config.Groups)
        {
            foreach (var entry in group.Entries)
            {
                var source = !string.IsNullOrWhiteSpace(entry.IconPath) && File.Exists(entry.IconPath)
                    ? entry.IconPath
                    : entry.Target;

                bool exists = !string.IsNullOrWhiteSpace(source)
                              && (File.Exists(source) || Directory.Exists(source));

                var sw = Stopwatch.StartNew();
                System.Windows.Media.ImageSource? image = null;
                try { image = IconService.Get(entry.Target, entry.IconPath); }
                catch (Exception ex) { sb.AppendLine($"{group.Name}|{entry.Name}|{source}|{exists}|EXCEPTION {ex.GetType().Name}|0|"); continue; }
                sw.Stop();

                string icon = image is null
                    ? "NULL"
                    : $"{image.GetType().Name} {((System.Windows.Media.Imaging.BitmapSource)image).PixelWidth}x{((System.Windows.Media.Imaging.BitmapSource)image).PixelHeight}";

                string fallback = !string.IsNullOrWhiteSpace(entry.IconPath) ? "iconFile" : "";
                string cacheKey = "";
                try
                {
                    var raw = System.Text.Encoding.UTF8.GetBytes("v4|" + source.ToLowerInvariant() + "|"
                        + (File.Exists(source) ? File.GetLastWriteTimeUtc(source).Ticks
                           : Directory.Exists(source) ? Directory.GetLastWriteTimeUtc(source).Ticks : 0));
                    cacheKey = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(raw))[..20];
                }
                catch { }
                sb.AppendLine($"{group.Name}|{entry.Name}|{source}|{exists}|{icon}|{sw.ElapsedMilliseconds}|{fallback}|{cacheKey}");
            }
        }

        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
    }
}
