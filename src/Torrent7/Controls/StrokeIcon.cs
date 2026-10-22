using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Torrent7.Controls;

/// <summary>
/// A Tabler outline icon: a path on a 24 box, stroked in whatever foreground it inherits, so
/// a button's hover and disabled colours reach the icon without extra styles.
/// </summary>
public sealed class StrokeIcon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<StrokeIcon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<StrokeIcon, IBrush?>(nameof(Stroke));

    /// <summary>The inherited text colour, so a button's hover colour reaches its icon.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<StrokeIcon>();

    public static readonly StyledProperty<double> BoxProperty =
        AvaloniaProperty.Register<StrokeIcon, double>(nameof(Box), 24);

    static StrokeIcon()
    {
        AffectsRender<StrokeIcon>(DataProperty, StrokeProperty, BoxProperty, ForegroundProperty);
    }

    public StrokeIcon()
    {
        Width = 16;
        Height = 16;
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>Overrides the inherited foreground, for the few icons with a colour of their own.</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>The size of the box the path was drawn on: 24 for Tabler, 20 for the mark.</summary>
    public double Box
    {
        get => GetValue(BoxProperty);
        set => SetValue(BoxProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Data is not { } data) return;
        var brush = Stroke ?? Foreground ?? Brushes.White;
        var scale = Math.Min(Bounds.Width, Bounds.Height) / Box;
        var pen = new Pen(brush, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            context.DrawGeometry(null, pen, data);
    }
}
