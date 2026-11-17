using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickBoard.Core;

/// <summary>
/// 界面外观：整体不透明度 + 自定义背景图 + 配色主题 + 图标尺寸。
/// 运行时替换 Theme.xaml 里的动态画刷与尺寸资源；所有引用必须用 DynamicResource。
/// </summary>
public static class Appearance
{
    private static readonly string[] TintKeys =
    {
        "PanelBrush", "SurfaceBrush", "TileBrush", "TileHoverBrush", "LineBrush", "SlotOutlineBrush"
    };

    public static double ClampOpacity(double value) => double.IsNaN(value) ? 1 : Math.Clamp(value, 0.2, 1.0);

    /// <summary>把 0/1/2 的主题档位解析成实际主题；2=跟随系统（读 AppsUseLightTheme）。</summary>
    public static int ResolveTheme(int themeKind)
    {
        if (themeKind == ThemeKinds.Light) return ThemeKinds.Light;
        if (themeKind == ThemeKinds.Dark) return ThemeKinds.Dark;
        return SystemUsesLight() ? ThemeKinds.Light : ThemeKinds.Dark;
    }

    private static bool SystemUsesLight()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>按不透明度重算画刷（沿用深色主题，兼容旧调用）。</summary>
    public static void Apply(double opacity) => Apply(opacity, ThemeKinds.Dark, IconScales.Standard);

    /// <summary>兼容旧调用：不带名称字号。</summary>
    public static void Apply(double opacity, int themeKind, int iconScale)
        => Apply(opacity, themeKind, iconScale, NameFontSizes.Default);

    /// <summary>
    /// 统一入口：一次设置不透明度、主题配色、图标尺寸档位与磁贴名称字号。
    /// </summary>
    public static void Apply(double opacity, int themeKind, int iconScale, double nameFontSize)
    {
        var app = Application.Current;
        if (app is null) return;

        double f = ClampOpacity(opacity);
        int theme = ResolveTheme(themeKind);
        bool light = theme == ThemeKinds.Light;

        if (light)
        {
            Set(app, "PanelBrush", A(f * 250, 0xF7, 0xF8, 0xFA));
            Set(app, "SurfaceBrush", A(f * 235, 0xFF, 0xFF, 0xFF));
            Set(app, "TileBrush", A(f * 30, 0x10, 0x20, 0x35));
            Set(app, "TileHoverBrush", A(f * 58, 0x10, 0x20, 0x35));
            Set(app, "LineBrush", A(f * 55, 0x20, 0x2A, 0x38));
            Set(app, "SlotOutlineBrush", A(f * 45, 0x20, 0x2A, 0x38));
            Set(app, "BgBrush", Hex("#FFFDFEFF"));
            Set(app, "TextBrush", Hex("#FF1E2430"));
            Set(app, "DimBrush", Hex("#FF5C6672"));
            Set(app, "AccentBrush", Hex("#FF2F6FE0"));
            Set(app, "FieldBgBrush", Hex("#FFEDF1F6"));
            Set(app, "PreviewBgBrush", Hex("#FFE4E9F0"));
            Set(app, "TooltipBgBrush", Hex("#FFF7F9FC"));
            Set(app, "TooltipBorderBrush", Hex("#FFC9D2DD"));
        }
        else
        {
            Set(app, "PanelBrush", A(f * 250, 0x15, 0x1A, 0x1F));
            Set(app, "SurfaceBrush", A(f * 225, 0x22, 0x28, 0x31));
            Set(app, "TileBrush", A(f * 22, 0xFF, 0xFF, 0xFF));
            Set(app, "TileHoverBrush", A(f * 62, 0xFF, 0xFF, 0xFF));
            Set(app, "LineBrush", A(f * 38, 0xFF, 0xFF, 0xFF));
            Set(app, "SlotOutlineBrush", A(f * 30, 0xFF, 0xFF, 0xFF));
            Set(app, "BgBrush", Hex("#FA151A1F"));
            Set(app, "TextBrush", Hex("#FFF1F3F6"));
            Set(app, "DimBrush", Hex("#FF98A0AC"));
            Set(app, "AccentBrush", Hex("#FF5B8DEF"));
            Set(app, "FieldBgBrush", Hex("#FF1B2027"));
            Set(app, "PreviewBgBrush", Hex("#FF11161C"));
            Set(app, "TooltipBgBrush", Hex("#F21A2027"));
            Set(app, "TooltipBorderBrush", Hex("#33FFFFFF"));
        }

        // 图标尺寸档位（sys:Double 资源，XAML 用 DynamicResource 绑定 Width/Height）
        double tile = iconScale == IconScales.Compact ? 36 : iconScale == IconScales.Large ? 54 : 44;
        double fav = iconScale == IconScales.Compact ? 26 : iconScale == IconScales.Large ? 40 : 32;
        app.Resources["TileIconSize"] = tile;
        app.Resources["FavIconSize"] = fav;

        // 磁贴名称字号：键盘磁贴为设定值，收藏栏小一档；行高/两行区高度随字号联动。
        // 上限 13 保证两行文字 (13+3)*2=32 恰好填满名称行，不改变按钮高 104 的布局基线。
        double nameFs = NameFontSizes.Clamp(nameFontSize);
        double favFs = Math.Max(8, nameFs - 1);
        app.Resources["TileNameFontSize"] = nameFs;
        app.Resources["TileNameLineHeight"] = nameFs + 3;
        app.Resources["TileNameMaxHeight"] = (nameFs + 3) * 2 + 2;
        app.Resources["FavNameFontSize"] = favFs;
        app.Resources["FavNameLineHeight"] = favFs + 3;
        app.Resources["FavNameMaxHeight"] = (favFs + 3) * 2;
    }

    /// <summary>加载背景图；路径无效时返回 null。默认铺满全图（兼容旧调用）。</summary>
    public static ImageBrush? LoadImageBrush(string? path) =>
        LoadImageBrush(path, 0, 0, 0, 1, 1);

    /// <summary>
    /// 加载背景图并按裁剪区域 / 铺放方式生成画刷。
    /// fit: 0=UniformToFill(铺满，配合裁剪)、1=Uniform(完整适应)、2=Fill(拉伸)。
    /// crop 为原图相对坐标 0–1，越界会被夹取。
    /// </summary>
    public static ImageBrush? LoadImageBrush(string? path, int fit,
        double cropX, double cropY, double cropW, double cropH, double opacity = 1)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            var stretch = fit switch
            {
                1 => Stretch.Uniform,
                2 => Stretch.Fill,
                _ => Stretch.UniformToFill
            };

            // 只在裁剪区域不是全图时设置 Viewbox，避免默认路径也走裁剪分支
            bool hasCrop = cropX > 0.0001 || cropY > 0.0001
                           || cropW < 0.9999 || cropH < 0.9999;
            var brush = new ImageBrush(bitmap) { Stretch = stretch };
            if (hasCrop)
            {
                double x = Math.Clamp(cropX, 0, 1);
                double y = Math.Clamp(cropY, 0, 1);
                double w = Math.Clamp(cropW, 0.01, 1 - x);
                double h = Math.Clamp(cropH, 0.01, 1 - y);
                brush.Viewbox = new Rect(x, y, w, h);
                brush.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
            }
            // 不透明度放在画刷上而不是元素的 Opacity 上：
            // 元素 Opacity<1 会让 WPF 为整块面板开一张离屏合成层（约 4MB/层），
            // 画刷透明度只是逐像素混合，不产生额外图面。
            brush.Opacity = Math.Clamp(opacity, 0, 1);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return null;
        }
    }

    private static Color A(double alpha, byte r, byte g, byte b) =>
        Color.FromArgb((byte)Math.Clamp(Math.Round(alpha), 0, 255), r, g, b);

    private static Color Hex(string argb) =>
        (Color)ColorConverter.ConvertFromString(argb);

    private static void Set(Application app, string key, Color color)
    {
        // 顶层资源会覆盖 Theme.xaml 合并字典里的同名项，DynamicResource 会立即刷新
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        app.Resources[key] = brush;
    }

    public static IEnumerable<string> TintKeysAll => TintKeys;
}
