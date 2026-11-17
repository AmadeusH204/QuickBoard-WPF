using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickBoard.Core;

/// <summary>
/// 图标提取：exe/dll/ico 优先从文件自身图标资源取 256×256（与原版 MadAppLauncher 同源思路，
/// 但请求高分辨率，解决 Viewbox 放大时的模糊）；.lnk 解析目标后同样处理；文件夹与失败项回退 Shell。
/// 结果缓存成 PNG。
/// </summary>
public static class IconService
{
    /// <summary>高分辨率图标边长。窗口经 Viewbox 放大 2~3 倍后单图标约 44~132 物理像素，256 源足够保持锐利。</summary>
    private const int IconSize = 256;

    private static string _cacheDirectory = "";

    /// <summary>内存缓存（key = 磁盘缓存名）：切组/重建时避免重复读盘解码 PNG。</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BitmapSource> MemoryCache = new();

    public static void Initialize(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
        Directory.CreateDirectory(cacheDirectory);

        // 缓存目录只增不减会越积越多（实测 156 个 PNG 里约 2/3 是历史残留），启动时后台修剪
        Task.Run(() => CleanupCache());
    }

    public static ImageSource? Get(string target, string iconPath)
    {
        var source = !string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath) ? iconPath : target;
        if (string.IsNullOrWhiteSpace(source)) return null;

        string key;
        try
        {
            key = BuildKey(source);
        }
        catch
        {
            key = "";
        }

        if (key.Length > 0 && MemoryCache.TryGetValue(key, out var hit)) return hit;

        string cacheFile = key.Length > 0 ? Path.Combine(_cacheDirectory, key + ".png") : "";
        try
        {
            if (cacheFile.Length > 0 && File.Exists(cacheFile))
            {
                var cached = LoadPng(cacheFile);
                if (cached is not null)
                {
                    Remember(key, cached);
                    return cached;
                }
            }
        }
        catch
        {
            // 缓存不可用时直接现取
        }

        var image = Extract(source);
        if (image is not null && cacheFile.Length > 0)
        {
            Remember(key, image);
            try { SavePng(image, cacheFile); } catch { }
        }
        return image;
    }

    /// <summary>内存缓存统计（诊断用）：条目数、按 BGRA32 估算的像素字节数、各图标尺寸分布。</summary>
    public static (int Count, long Bytes, string Sizes) DescribeCache()
    {
        long bytes = 0;
        var sizes = new SortedDictionary<string, int>();
        foreach (var kv in MemoryCache)
        {
            var bmp = kv.Value;
            bytes += (long)bmp.PixelWidth * bmp.PixelHeight * 4;
            var tag = $"{bmp.PixelWidth}x{bmp.PixelHeight}";
            sizes[tag] = sizes.TryGetValue(tag, out var n) ? n + 1 : 1;
        }
        var text = string.Join(", ", sizes.Select(kv => $"{kv.Key}×{kv.Value}"));
        return (MemoryCache.Count, bytes, text);
    }

    /// <summary>清空内存缓存（诊断用：用于测量图标像素占用的真实内存）。</summary>
    public static void ClearMemoryCache() => MemoryCache.Clear();

    private const int MemoryCacheLimit = 256;

    private static void Remember(string key, BitmapSource image)
    {
        if (key.Length == 0) return;
        // 图标总数通常几十个；超过上限时整体清空，避免长期运行后无限增长
        if (MemoryCache.Count > MemoryCacheLimit) MemoryCache.Clear();
        MemoryCache[key] = image;
    }

    /// <summary>
    /// 修剪磁盘缓存：按最后写入时间保留最新的 keep 个，其余删除（删除的会按需重新提取）。
    /// 只在文件数超限时动手，正常规模不产生任何 IO 删除。
    /// </summary>
    private static void CleanupCache(int keep = 200)
    {
        try
        {
            var dir = new DirectoryInfo(_cacheDirectory);
            if (!dir.Exists) return;

            var files = dir.GetFiles("*.png");
            if (files.Length <= keep) return;

            foreach (var file in files.OrderByDescending(f => f.LastWriteTimeUtc).Skip(keep))
            {
                try { file.Delete(); } catch { }
            }
        }
        catch
        {
            // 清理失败不影响图标加载
        }
    }

    private static string BuildKey(string source)
    {
        long stamp = 0;
        try
        {
            if (File.Exists(source)) stamp = File.GetLastWriteTimeUtc(source).Ticks;
            else if (Directory.Exists(source)) stamp = Directory.GetLastWriteTimeUtc(source).Ticks;
        }
        catch { }

        // v4：图标源从 32×32 升级到 256×256，旧缓存需失效重新提取
        var raw = System.Text.Encoding.UTF8.GetBytes("v4|" + source.ToLowerInvariant() + "|" + stamp);
        var hash = System.Security.Cryptography.SHA1.HashData(raw);
        return Convert.ToHexString(hash)[..20];
    }

    private static BitmapSource? Extract(string source)
    {
        // ① 文件自身图标资源（原版核心设计）：exe/dll/ico 直接取文件内第 0 个图标资源，
        //    请求 256px，避免 Shell 缩略图返回的错误图标/画布小图。
        //    .lnk 无自身资源，先解析到真实目标再取。
        string? resourceFile = null;
        if (source.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = ShortcutResolver.ResolveTarget(source);
            if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved)) resourceFile = resolved;
        }
        else if (File.Exists(source))
        {
            resourceFile = source;
        }

        if (resourceFile is not null)
        {
            var hi = ExtractFromFileResource(resourceFile);
            if (hi is not null) return hi;
        }

        // ② Shell 高清图标（文件夹、无法从资源提取的文件与 .lnk 回退）
        Native.IShellItemImageFactory? factory = null;
        try
        {
            var iid = typeof(Native.IShellItemImageFactory).GUID;
            Native.SHCreateItemFromParsingName(source, IntPtr.Zero, iid, out factory);
        }
        catch
        {
            factory = null;
        }

        if (factory is not null)
        {
            try
            {
                foreach (var size in new[] { 256, 96, 48, 32 })
                {
                    try
                    {
                        int hr = factory.GetImage(new Native.SIZE(size, size), Native.SIIGBF.ICONONLY, out var hBitmap);
                        if (hr != 0 || hBitmap == IntPtr.Zero) continue;

                        try
                        {
                            var image = Imaging.CreateBitmapSourceFromHBitmap(
                                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            image.Freeze();
                            return image;
                        }
                        finally
                        {
                            Native.DeleteObject(hBitmap);
                        }
                    }
                    catch
                    {
                        // 换下一个尺寸 / 走回退
                    }
                }
            }
            finally
            {
                // 显式释放 COM 引用，避免每次提取都留一个 RCW
                try { System.Runtime.InteropServices.Marshal.ReleaseComObject(factory); } catch { }
            }
        }

        // 回退一：SHGetFileInfo（对文件夹、快捷方式都稳）
        try
        {
            var info = new Native.SHFILEINFO();
            uint attributes = Directory.Exists(source) ? Native.FILE_ATTRIBUTE_DIRECTORY : Native.FILE_ATTRIBUTE_NORMAL;
            var flags = Native.SHGFI_ICON | Native.SHGFI_LARGEICON
                        | (File.Exists(source) || Directory.Exists(source) ? 0 : Native.SHGFI_USEFILEATTRIBUTES);

            var result = Native.SHGetFileInfo(source, attributes, ref info, (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.SHFILEINFO>(), flags);
            if (result != IntPtr.Zero && info.hIcon != IntPtr.Zero)
            {
                try
                {
                    var image = Imaging.CreateBitmapSourceFromHIcon(
                        info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    image.Freeze();
                    return image;
                }
                finally
                {
                    Native.DestroyIcon(info.hIcon);
                }
            }
        }
        catch
        {
            // 继续回退
        }

        // 回退二：从可执行文件里直接抽
        try
        {
            if (File.Exists(source))
            {
                var ico = System.Drawing.Icon.ExtractAssociatedIcon(source);
                if (ico is not null)
                {
                    try
                    {
                        var image = Imaging.CreateBitmapSourceFromHIcon(
                            ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        image.Freeze();
                        return image;
                    }
                    finally
                    {
                        ico.Dispose();
                    }
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// 从文件自身的图标资源提取高分辨率图标。
    /// ExtractIconEx 只能拿系统"大图标"(32×32)；同一个资源索引用 PrivateExtractIcons 请求 256
    /// 可拿到 256×256 版本，图标身份一致（IDEA 仍是 IJ 图标而非通用方块）。
    /// </summary>
    private static BitmapSource? ExtractFromFileResource(string file)
    {
        try
        {
            var icons = new IntPtr[1];
            var ids = new uint[1];
            uint n = Native.PrivateExtractIcons(file, 0, IconSize, IconSize, icons, ids, 1, 0);
            if (n > 0 && icons[0] != IntPtr.Zero)
            {
                try
                {
                    var img = Imaging.CreateBitmapSourceFromHIcon(
                        icons[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    img.Freeze();
                    return img;
                }
                finally
                {
                    Native.DestroyIcon(icons[0]);
                }
            }
        }
        catch
        {
            // 继续用 ExtractIconEx 保底
        }

        // 回退：ExtractIconEx(file, 0) —— 与原版完全一致的语义（32×32 保底）
        try
        {
            uint count = Native.ExtractIconEx(file, 0, out var large, out var small, 1);
            if (count > 0 && large != IntPtr.Zero)
            {
                try
                {
                    var img = Imaging.CreateBitmapSourceFromHIcon(
                        large, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    img.Freeze();
                    return img;
                }
                finally
                {
                    Native.DestroyIcon(large);
                    if (small != IntPtr.Zero) Native.DestroyIcon(small);
                }
            }
        }
        catch { }

        return null;
    }

    private static BitmapSource? LoadPng(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static void SavePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
