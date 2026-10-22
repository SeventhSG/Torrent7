using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Torrent7.Controls;

/// <summary>
/// Which parts of the torrent are on disk, left to right. Each bucket covers a run of
/// pieces and is drawn as solid as the share of them that is done, so gaps read at a glance.
/// </summary>
public sealed class PieceBar : Control
{
    public static readonly StyledProperty<double[]?> BucketsProperty =
        AvaloniaProperty.Register<PieceBar, double[]?>(nameof(Buckets));

    public static readonly StyledProperty<int> VersionProperty =
        AvaloniaProperty.Register<PieceBar, int>(nameof(Version));

    static PieceBar()
    {
        AffectsRender<PieceBar>(BucketsProperty, VersionProperty);
    }

    public double[]? Buckets { get => GetValue(BucketsProperty); set => SetValue(BucketsProperty, value); }
    public int Version { get => GetValue(VersionProperty); set => SetValue(VersionProperty, value); }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var track = this.FindResource("RaisedColor") is Color t ? t : Colors.DimGray;
        var fill = this.FindResource("AccentColor") is Color a ? a : Colors.Orange;
        var clip = new RoundedRect(new Rect(0, 0, w, h), 3);
        using (context.PushClip(clip))
        {
            context.DrawRectangle(new SolidColorBrush(track), null, new Rect(0, 0, w, h));
            var buckets = Buckets;
            if (buckets == null || buckets.Length == 0) return;
            var step = w / buckets.Length;
            for (var i = 0; i < buckets.Length; i++)
            {
                var v = buckets[i];
                if (v <= 0) continue;
                // A whole bucket is full amber; a partial one fades toward the track.
                var alpha = v >= 1 ? 1 : 0.25 + 0.6 * v;
                context.DrawRectangle(new SolidColorBrush(fill, alpha), null, new Rect(i * step, 0, Math.Ceiling(step + 0.5), h));
            }
        }
    }
}
