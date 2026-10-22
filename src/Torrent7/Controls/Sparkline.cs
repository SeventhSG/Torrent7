using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Torrent7.Core;

namespace Torrent7.Controls;

/// <summary>
/// Download and upload over the last couple of minutes. Download is a filled amber area,
/// upload a thin line in the quiet colour, both on the same scale so they compare honestly.
/// </summary>
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<SpeedHistory?> HistoryProperty =
        AvaloniaProperty.Register<Sparkline, SpeedHistory?>(nameof(History));

    /// <summary>Bumped by the view model every tick; the history itself mutates in place.</summary>
    public static readonly StyledProperty<int> VersionProperty =
        AvaloniaProperty.Register<Sparkline, int>(nameof(Version));

    public static readonly StyledProperty<bool> ShowUploadProperty =
        AvaloniaProperty.Register<Sparkline, bool>(nameof(ShowUpload), true);

    static Sparkline()
    {
        AffectsRender<Sparkline>(HistoryProperty, VersionProperty, ShowUploadProperty);
    }

    public SpeedHistory? History { get => GetValue(HistoryProperty); set => SetValue(HistoryProperty, value); }
    public int Version { get => GetValue(VersionProperty); set => SetValue(VersionProperty, value); }
    public bool ShowUpload { get => GetValue(ShowUploadProperty); set => SetValue(ShowUploadProperty, value); }

    public override void Render(DrawingContext context)
    {
        var history = History;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (history == null || history.Count < 2 || w <= 0 || h <= 0) return;

        var samples = history.Samples().ToArray();
        // A floor of 64 KiB/s keeps a trickle from filling the whole box.
        var peak = Math.Max(64 * 1024, samples.Max(s => Math.Max(s.Down, ShowUpload ? s.Up : 0)));
        var step = w / (history.Capacity - 1);
        var x0 = w - (samples.Length - 1) * step;

        Point At(int i, long v) => new(x0 + i * step, h - 1 - (h - 2) * v / (double)peak);

        var accent = this.FindResource("AccentColor") is Color a ? a : Colors.Orange;
        var up = this.FindResource("UpColor") is Color u ? u : Colors.Gray;

        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(x0, h), true);
            for (var i = 0; i < samples.Length; i++) g.LineTo(At(i, samples[i].Down));
            g.LineTo(new Point(x0 + (samples.Length - 1) * step, h));
            g.EndFigure(true);
        }
        context.DrawGeometry(new SolidColorBrush(accent, 0.18), null, area);

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(At(0, samples[0].Down), false);
            for (var i = 1; i < samples.Length; i++) g.LineTo(At(i, samples[i].Down));
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(new SolidColorBrush(accent), 1.5, lineJoin: PenLineJoin.Round), line);

        if (!ShowUpload) return;
        var upLine = new StreamGeometry();
        using (var g = upLine.Open())
        {
            g.BeginFigure(At(0, samples[0].Up), false);
            for (var i = 1; i < samples.Length; i++) g.LineTo(At(i, samples[i].Up));
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(new SolidColorBrush(up, 0.8), 1, lineJoin: PenLineJoin.Round), upLine);
    }
}
