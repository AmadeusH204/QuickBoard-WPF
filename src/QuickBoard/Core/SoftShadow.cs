using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickBoard.Core;

/// <summary>
/// 程序生成柔和投影，替代 WPF 的 <c>DropShadowEffect</c>。
/// </summary>
/// <remarks>
/// 为什么不用 DropShadowEffect：主窗口是分层窗口（AllowsTransparency=True），WPF 走软件渲染，
/// 给一整块面板加模糊特效会分配多张同尺寸中间位图。实测 1715×752 窗口下，带特效应比不带多约 48MB，
/// 且与模糊半径几乎无关（半径 20 → 8 仍多 47MB），只能整块换掉。
/// 这里把"圆角矩形 + 高斯衰减 alpha"一次性算成半分辨率位图（约 1MB），用 ImageBrush 铺开，
/// 观感等价，但没有逐帧特效缓冲。只有尺寸变化时重算。
/// </remarks>
public static class SoftShadow
{
    /// <summary>
    /// 生成阴影画刷：画布 <paramref name="width"/>×<paramref name="height"/>，
    /// 被"投影"的圆角矩形内缩 <paramref name="padding"/>，透明度沿边界按高斯衰减。
    /// </summary>
    /// <param name="width">画布逻辑宽度（= 面板宽 + 2×padding）。</param>
    /// <param name="height">画布逻辑高度。</param>
    /// <param name="padding">面板相对画布的内缩量（也就是阴影外扩宽度）。</param>
    /// <param name="radius">面板圆角半径。</param>
    /// <param name="blur">柔和程度，对应原 BlurRadius。</param>
    /// <param name="offsetY">阴影下移量，模拟光从上方来。</param>
    /// <param name="opacity">最深处不透明度。</param>
    public static ImageBrush Create(double width, double height, double padding, double radius,
        double blur = 20, double offsetY = 4, double opacity = 0.55)
    {
        if (width < 8 || height < 8) width = height = 8;

        // 半分辨率：影子本身就是模糊的，内存只要 1/4
        const double scale = 0.5;
        int w = Math.Max(4, (int)Math.Ceiling(width * scale));
        int h = Math.Max(4, (int)Math.Ceiling(height * scale));

        double sigma = Math.Max(0.8, blur * 0.5) * scale;
        double r = Math.Max(0, radius) * scale;
        double pad = Math.Max(0, padding) * scale;
        double shift = offsetY * scale;

        // 面板矩形（阴影元素坐标）
        double left = pad;
        double top = pad + shift;
        double right = Math.Max(left + 1, w - pad);
        double bottom = Math.Max(top + 1, h - pad + shift);

        double cx = (left + right) / 2;
        double cy = (top + bottom) / 2;
        double halfW = (right - left) / 2;
        double halfH = (bottom - top) / 2;
        double innerHalfW = Math.Max(0, halfW - r);
        double innerHalfH = Math.Max(0, halfH - r);

        var pixels = new byte[w * h * 4];

        for (int y = 0; y < h; y++)
        {
            double qy = Math.Abs(y + 0.5 - cy) - innerHalfH;
            for (int x = 0; x < w; x++)
            {
                double qx = Math.Abs(x + 0.5 - cx) - innerHalfW;

                // 圆角矩形的有符号距离（<0 在内部）
                double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
                double distance = Math.Min(Math.Max(qx, qy), 0)
                                  + Math.Sqrt(ox * ox + oy * oy) - r;

                // 模糊轮廓的透明度分布 = 高斯累积分布：边界处恰好一半，向内趋于满，向外渐隐
                double a = distance <= 0 && distance < -3 * sigma
                    ? 1.0
                    : 0.5 * (1 + Erf(-distance / (sigma * Math.Sqrt(2))));

                byte alpha = (byte)Math.Clamp(a * opacity * 255, 0, 255);
                int i = (y * w + x) * 4;
                pixels[i] = 0;       // B
                pixels[i + 1] = 0;   // G
                pixels[i + 2] = 0;   // R
                pixels[i + 3] = alpha;
            }
        }

        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        bitmap.Freeze();

        var brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
        brush.Freeze();
        return brush;
    }

    /// <summary>误差函数（Abramowitz &amp; Stegun 7.1.26 近似，误差 &lt; 1.5e-7）。</summary>
    private static double Erf(double x)
    {
        double sign = x < 0 ? -1 : 1;
        x = Math.Abs(x);
        const double a1 = 0.254829592, a2 = -0.284496736, a3 = 1.421413741,
                     a4 = -1.453152027, a5 = 1.061405429, p = 0.3275911;
        double t = 1.0 / (1.0 + p * x);
        double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
        return sign * y;
    }
}
