using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Torrent7.Controls;

/// <summary>
/// A thin progress meter that takes exactly the width it is given, for table cells where
/// the stock progress bar's minimum width would spill into the next column.
/// </summary>
public sealed class Meter : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<Meter, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<Meter, IBrush?>(nameof(Fill));

    static Meter()
    {
        AffectsRender<Meter>(ValueProperty, FillProperty);
    }

    public Meter()
    {
        Height = 4;
    }

    /// <summary>0 to 100.</summary>
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var r = h / 2;
        // A translucent track reads on any row background, selected or not.
        var track = new SolidColorBrush(Colors.White, 0.09);
        context.DrawRectangle(track, null, new RoundedRect(new Rect(0, 0, w, h), r));
        var v = Math.Clamp(Value, 0, 100) / 100;
        if (v <= 0) return;
        var fill = Fill ?? this.FindResource("AccentBrush") as IBrush ?? Brushes.Orange;
        context.DrawRectangle(fill, null, new RoundedRect(new Rect(0, 0, Math.Max(h, w * v), h), r));
    }
}
