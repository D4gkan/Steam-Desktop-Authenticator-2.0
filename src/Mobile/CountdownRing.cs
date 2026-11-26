using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Sda.Mobile;

public sealed class CountdownRing : Control
{
    public static readonly StyledProperty<int> SecondsProperty = AvaloniaProperty.Register<CountdownRing, int>(nameof(Seconds), 30);
    public int Seconds { get => GetValue(SecondsProperty); set => SetValue(SecondsProperty, value); }
    static CountdownRing() => AffectsRender<CountdownRing>(SecondsProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - 3;
        context.DrawEllipse(null, new Pen(Brush.Parse("#30283E"), 2.5), center, radius, radius);
        var points = Math.Clamp(Seconds, 0, 30) * 3;
        if (points > 0)
        {
            var geometry = new StreamGeometry();
            using (var drawing = geometry.Open())
            {
                drawing.BeginFigure(new Point(center.X, center.Y - radius), false);
                for (var step = 1; step <= points; step++)
                {
                    var angle = -Math.PI / 2 + step / 90.0 * Math.PI * 2;
                    drawing.LineTo(new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius));
                }
                drawing.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(Brush.Parse("#A48ACE"), 2.5), geometry);
        }
    }
}
