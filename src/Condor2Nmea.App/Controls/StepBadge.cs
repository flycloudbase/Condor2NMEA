using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Condor2Nmea.App.Controls;

/// <summary>Round step number that turns into a green check mark when the step is done.</summary>
public sealed class StepBadge : FrameworkElement
{
    public static readonly DependencyProperty NumberProperty = DependencyProperty.Register(
        nameof(Number), typeof(int), typeof(StepBadge), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsDoneProperty = DependencyProperty.Register(
        nameof(IsDone), typeof(bool), typeof(StepBadge), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Number
    {
        get => (int)GetValue(NumberProperty);
        set => SetValue(NumberProperty, value);
    }

    public bool IsDone
    {
        get => (bool)GetValue(IsDoneProperty);
        set => SetValue(IsDoneProperty, value);
    }

    private static readonly Brush Done = Freeze(new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50)));

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    protected override Size MeasureOverride(Size availableSize) => new(36, 36);

    protected override void OnRender(DrawingContext dc)
    {
        var center = new Point(18, 18);
        if (IsDone)
        {
            dc.DrawEllipse(Done, null, center, 17, 17);
            var check = new StreamGeometry();
            using (var g = check.Open())
            {
                g.BeginFigure(new Point(10.5, 18.5), false, false);
                g.LineTo(new Point(16, 24), true, true);
                g.LineTo(new Point(26, 12.5), true, true);
            }
            check.Freeze();
            var pen = new Pen(Brushes.White, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, check);
            return;
        }
        var fg = TextElement.GetForeground(this) ?? Brushes.White;
        var ring = new Pen(fg, 2);
        ring.Freeze();
        dc.PushOpacity(0.8);
        dc.DrawEllipse(null, ring, center, 16, 16);
        var text = new FormattedText(Number.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 16, fg,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
        dc.Pop();
    }
}
