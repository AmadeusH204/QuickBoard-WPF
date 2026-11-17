using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace QuickBoard.Core;

/// <summary>
/// 支持真描边的文字控件：黑字主体 + 细描边，保证名称在任何壁纸上可读。
/// WPF 原生 TextBlock 不支持 Stroke，这里用 FormattedText.BuildGeometry + Pen 绘制。
/// 测量阶段委托给一个不在视觉树内的 TextBlock 代理，确保 desired 行为与 TextBlock 完全一致，
/// 不影响外层 UniformGrid 的列宽基线；渲染阶段按 Arrange 后的实际宽度绘制描边 + 填充。
/// </summary>
public class OutlinedTextBlock : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(OutlinedTextBlock),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(OutlinedTextBlock), new FrameworkPropertyMetadata(new FontFamily("Microsoft YaHei UI"),
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(OutlinedTextBlock), new FrameworkPropertyMetadata(11.0,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(OutlinedTextBlock), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(OutlinedTextBlock),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(OutlinedTextBlock),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextAlignmentProperty = TextBlock.TextAlignmentProperty.AddOwner(
        typeof(OutlinedTextBlock), new FrameworkPropertyMetadata(TextAlignment.Left, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextWrappingProperty = TextBlock.TextWrappingProperty.AddOwner(
        typeof(OutlinedTextBlock), new FrameworkPropertyMetadata(TextWrapping.NoWrap, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty TextTrimmingProperty = TextBlock.TextTrimmingProperty.AddOwner(
        typeof(OutlinedTextBlock), new FrameworkPropertyMetadata(TextTrimming.None, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty LineHeightProperty = TextBlock.LineHeightProperty.AddOwner(
        typeof(OutlinedTextBlock), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public TextAlignment TextAlignment { get => (TextAlignment)GetValue(TextAlignmentProperty); set => SetValue(TextAlignmentProperty, value); }
    public TextWrapping TextWrapping { get => (TextWrapping)GetValue(TextWrappingProperty); set => SetValue(TextWrappingProperty, value); }
    public TextTrimming TextTrimming { get => (TextTrimming)GetValue(TextTrimmingProperty); set => SetValue(TextTrimmingProperty, value); }
    public double LineHeight { get => (double)GetValue(LineHeightProperty); set => SetValue(LineHeightProperty, value); }

    private TextBlock? _measureProxy;

    protected override Size MeasureOverride(Size availableSize)
    {
        // 用真实 TextBlock 代理测量，desired 与 TextBlock 完全一致（含 Wrap/Trimming 语义）
        _measureProxy ??= new TextBlock();
        var p = _measureProxy;
        p.Text = Text;
        p.FontFamily = FontFamily;
        p.FontSize = FontSize;
        p.TextAlignment = TextAlignment;
        p.TextWrapping = TextWrapping;
        p.TextTrimming = TextTrimming;
        p.LineHeight = LineHeight;
        p.Measure(availableSize);
        var desired = p.DesiredSize;
        return new Size(desired.Width, desired.Height);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var text = Text;
        if (string.IsNullOrEmpty(text)) return;
        var width = Math.Max(ActualWidth, 1);
        var ft = BuildFormattedText(width, ActualHeight);
        var geometry = ft.BuildGeometry(new Point(0, 0));
        if (geometry is null || geometry.IsEmpty()) return;

        geometry.Freeze();

        // 先画描边层，再画填充层，黑字主体覆盖描边内侧
        var stroke = Stroke;
        if (stroke is not null && StrokeThickness > 0)
        {
            var pen = new Pen(stroke, StrokeThickness);
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
        dc.DrawGeometry(Foreground, null, geometry);
    }

    /// <summary>
    /// 构建排版对象：宽度按实际可用宽度折行，行数按实际高度截断。
    /// 行数必须在这里限死并带省略号——否则超出的行会被父级 ClipToBounds 无声切掉
    /// （表现为「名字很长却没有省略号、最后一行整个消失」）。
    /// </summary>
    private FormattedText BuildFormattedText(double maxWidth, double maxHeight)
    {
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var fill = Foreground ?? Brushes.Black;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var ft = new FormattedText(Text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, FontSize, fill, pixelsPerDip)
        {
            TextAlignment = TextAlignment,
            Trimming = TextTrimming,
        };

        // 先定行高（未限宽时的 Height 就是一行的高度），再用它推算能放几行
        if (!double.IsNaN(LineHeight) && LineHeight > 0) ft.LineHeight = LineHeight;
        double lineHeight = ft.Height > 0 ? ft.Height : Math.Max(FontSize * 1.3, 1);

        if (TextWrapping == TextWrapping.Wrap && maxWidth > 0) ft.MaxTextWidth = maxWidth;

        if (maxHeight > 0)
        {
            int maxLines = Math.Max(1, (int)Math.Floor((maxHeight + 0.5) / lineHeight));
            ft.MaxLineCount = maxLines;
        }

        return ft;
    }
}
