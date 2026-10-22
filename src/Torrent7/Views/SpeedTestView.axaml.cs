using Avalonia.Controls;

namespace Torrent7.Views;

public sealed partial class SpeedTestView : UserControl
{
    public SpeedTestView()
    {
        InitializeComponent();
        // Side by side when there is room, one above the other when there is not.
        SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 900;
            Grid.SetColumn(HistoryCard, narrow ? 0 : 2);
            Grid.SetRow(HistoryCard, narrow ? 1 : 0);
            Grid.SetColumnSpan(HistoryCard, narrow ? 3 : 1);
            Grid.SetColumnSpan(TuneCard, narrow ? 3 : 1);
            HistoryCard.Margin = new Avalonia.Thickness(0, narrow ? 16 : 0, 0, 0);
        };
    }
}
