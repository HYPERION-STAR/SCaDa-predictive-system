using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SCaDaDashboard.Controls;

/// <summary>
/// Hafif, bagimsiz (harici NuGet gerektirmeyen) canli cizgi grafigi.
/// Values dizisi her tik degistiginde yeniden cizer; alan altini yumusak
/// bir gradyanla doldurur. Endustriyel sensor trendleri icin idealdir.
/// </summary>
public sealed class MiniChart : Control
{
    static MiniChart()
    {
        // Sablon gerekmez; dogrudan OnRender ile cizeriz.
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(MiniChart), new FrameworkPropertyMetadata(typeof(MiniChart)));
    }

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IEnumerable<double>), typeof(MiniChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<double>? Values
    {
        get => (IEnumerable<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(MiniChart),
        new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (Background != null)
            dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));

        var vals = Values as IList<double> ?? Values?.ToList();
        if (vals == null || vals.Count < 2 || w <= 2 || h <= 2)
            return;

        double min = double.MaxValue, max = double.MinValue;
        foreach (var v in vals)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }
        if (max - min < 1e-6) { min -= 1; max += 1; }

        const double pad = 6;
        double innerW = w - 2 * pad, innerH = h - 2 * pad;
        int n = vals.Count;

        double X(int i) => pad + innerW * i / (n - 1);
        double Y(double v) => pad + innerH * (1 - (v - min) / (max - min));

        // Cizgi geometrisi
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(new Point(X(0), Y(vals[0])), false, false);
            for (int i = 1; i < n; i++)
                ctx.LineTo(new Point(X(i), Y(vals[i])), true, true);
        }
        line.Freeze();

        // Alan dolgusu (cizgiden asagi)
        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(X(0), h - pad), true, true);
            ctx.LineTo(new Point(X(0), Y(vals[0])), true, true);
            for (int i = 1; i < n; i++)
                ctx.LineTo(new Point(X(i), Y(vals[i])), true, true);
            ctx.LineTo(new Point(X(n - 1), h - pad), true, true);
        }
        area.Freeze();

        var baseColor = (LineBrush as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;
        var fill = new LinearGradientBrush(
            Color.FromArgb(70, baseColor.R, baseColor.G, baseColor.B),
            Color.FromArgb(0, baseColor.R, baseColor.G, baseColor.B),
            new Point(0, 0), new Point(0, 1));
        fill.Freeze();
        dc.DrawGeometry(fill, null, area);

        var pen = new Pen(LineBrush, 1.8) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, line);

        // Son noktada nokta isareti (soluk hale + dolu nokta)
        var last = new Point(X(n - 1), Y(vals[n - 1]));
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(60, baseColor.R, baseColor.G, baseColor.B)),
            null, last, 5.5, 5.5);
        dc.DrawEllipse(LineBrush, null, last, 2.6, 2.6);
    }
}
