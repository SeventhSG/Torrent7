using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Torrent7.Controls;

/// <summary>
/// The app icon drawn as vectors, so it is sharp at any size inside the app: the amber tile
/// and the 7 that turns into a download arrow, from MarkPath in the tokens.
/// </summary>
public sealed class AppMark : Control
{
    private static readonly IBrush Tile = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromRgb(242, 172, 66), 0), new GradientStop(Color.FromRgb(219, 140, 36), 1) },
    };

    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(17, 18, 21));

    public AppMark()
    {
        Width = 22;
        Height = 22;
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        context.DrawRectangle(Tile, null, new RoundedRect(new Rect(0, 0, size, size), size * 0.22));
        if (this.FindResource("MarkPath") is not Geometry mark) return;
        var scale = size / 100;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            context.DrawGeometry(Ink, null, mark);
    }
}
