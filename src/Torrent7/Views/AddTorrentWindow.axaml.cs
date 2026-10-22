using Avalonia.Controls;
using Torrent7.ViewModels;

namespace Torrent7.Views;

public sealed partial class AddTorrentWindow : Window
{
    public AddTorrentWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is AddTorrentViewModel vm) vm.CloseRequested += Close;
        };
        Closed += (_, _) => (DataContext as AddTorrentViewModel)?.Closed();
    }
}
