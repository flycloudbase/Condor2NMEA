using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Condor2Nmea.App.Controls;

/// <summary>Traffic position relative to the own glider (metres, north-up).</summary>
public sealed record RadarTarget(double North, double East, double Vertical, string Label, double? Track);

/// <summary>Simple north-up FLARM-style radar.</summary>
public sealed class RadarView : FrameworkElement
{
    public static readonly DependencyProperty TargetsProperty = DependencyProperty.Register(
        nameof(Targets), typeof(IReadOnlyList<RadarTarget>), typeof(RadarView),
        new FrameworkPropertyMetadata(Array.Empty<RadarTarget>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RangeKmProperty = DependencyProperty.Register(
        nameof(RangeKm), typeof(double), typeof(RadarView),
        new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<RadarTarget> Targets
    {
        get => (IReadOnlyList<RadarTarget>)GetValue(TargetsProperty);
        set => SetValue(TargetsProperty, value);
    }

    public double RangeKm
    {
        get => (double)GetValue(RangeKmProperty);
        set => SetValue(RangeKmProperty, value);
    }

    private static readonly Brush Above = Frozen(Color.FromRgb(0xFF, 0x8A, 0x65));
    private static readonly Brush Level = Frozen(Color.FromRgb(0xFF, 0xD5, 0x4F));
    private static readonly Brush Below = Frozen(Color.FromRgb(0x81, 0xC7, 0x84));
    private static readonly Brush Own = Frozen(Color.FromRgb(0x4C, 0xC2, 0xFF));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var s = Math.Min(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width,
                         double.IsInfinity(availableSize.Height) ? 400 : availableSize.Height);
        return new Size(s, s);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var fg = TextElement.GetForeground(this) ?? Brushes.White;
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size < 50)
            return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = size / 2 - 18;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");

        // background and range rings
        var ringPen = new Pen(fg, 1) { DashStyle = DashStyles.Dash };
        ringPen.Freeze();
        dc.PushOpacity(0.35);
        dc.DrawEllipse(null, ringPen, center, radius, radius);
        dc.DrawEllipse(null, ringPen, center, radius / 2, radius / 2);
        dc.DrawLine(ringPen, new Point(center.X, center.Y - radius), new Point(center.X, center.Y + radius));
        dc.DrawLine(ringPen, new Point(center.X - radius, center.Y), new Point(center.X + radius, center.Y));
        dc.Pop();

        void Text(string text, Point at, Brush brush, double sizePt, bool centerAlign = false, FontWeight? weight = null)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(typeface.FontFamily, FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal), sizePt, brush, dpi);
            dc.DrawText(ft, centerAlign ? new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2) : at);
        }

        Text("N", new Point(center.X, center.Y - radius - 10), fg, 12, true, FontWeights.SemiBold);
        var range = Math.Max(0.5, RangeKm);
        Text($"{range:0.#} km", new Point(center.X + radius * 0.71 + 2, center.Y - radius * 0.71 - 14), fg, 11);
        Text($"{range / 2:0.#}", new Point(center.X + radius * 0.35 + 2, center.Y - radius * 0.35 - 14), fg, 11);

        // own glider
        var own = new StreamGeometry();
        using (var g = own.Open())
        {
            g.BeginFigure(new Point(center.X, center.Y - 9), true, true);
            g.LineTo(new Point(center.X + 7, center.Y + 7), true, false);
            g.LineTo(new Point(center.X, center.Y + 3), true, false);
            g.LineTo(new Point(center.X - 7, center.Y + 7), true, false);
        }
        own.Freeze();
        dc.DrawGeometry(Own, null, own);

        var scale = radius / (range * 1000);
        foreach (var t in Targets ?? Array.Empty<RadarTarget>())
        {
            var dist = Math.Sqrt(t.North * t.North + t.East * t.East);
            var x = t.East * scale;
            var y = -t.North * scale;
            var outside = dist > range * 1000;
            if (outside)
            {
                // clamp to the edge so far traffic stays visible
                var f = radius / Math.Max(1, Math.Sqrt(x * x + y * y));
                x *= f;
                y *= f;
            }
            var p = new Point(center.X + x, center.Y + y);
            var brush = t.Vertical > 50 ? Above : t.Vertical < -50 ? Below : Level;
            dc.PushOpacity(outside ? 0.45 : 1.0);
            dc.DrawEllipse(brush, null, p, 6, 6);
            if (t.Track is { } track)
            {
                var a = track * Math.PI / 180;
                var pen = new Pen(brush, 2);
                pen.Freeze();
                dc.DrawLine(pen, p, new Point(p.X + Math.Sin(a) * 16, p.Y - Math.Cos(a) * 16));
            }
            Text($"{t.Label}  {t.Vertical:+0;-0;0} m", new Point(p.X + 9, p.Y - 8), fg, 12);
            dc.Pop();
        }
    }
}
