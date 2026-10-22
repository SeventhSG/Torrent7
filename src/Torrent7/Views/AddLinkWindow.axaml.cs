using Avalonia.Controls;
using Avalonia.Interactivity;
using Torrent7.Core;

namespace Torrent7.Views;

/// <summary>A box for one or more links. Says straight away whether what is in it will work.</summary>
public sealed partial class AddLinkWindow : Window
{
    public AddLinkWindow() : this(null) { }

    public AddLinkWindow(string? suggestion)
    {
        InitializeComponent();
        LinkBox.Text = suggestion ?? "";
        LinkBox.TextChanged += (_, _) => Check();
        Opened += (_, _) =>
        {
            LinkBox.Focus();
            LinkBox.SelectAll();
        };
        Check();
    }

    private void Check()
    {
        var lines = (LinkBox.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var good = lines.Count(l => TorrentInput.Classify(l).Kind != TorrentInputKind.None);
        var bad = lines.Length - good;
        AddButton.IsEnabled = good > 0;
        Problem.Text = lines.Length == 0 ? " "
            : bad == 0 ? (good == 1 ? "Looks good." : $"{good} links to add.")
            : bad == lines.Length ? "That is not a magnet link, info hash or torrent link."
            : $"{good} will be added, {bad} {(bad == 1 ? "line is" : "lines are")} not a link and will be skipped.";
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
    private void OnAdd(object? sender, RoutedEventArgs e) => Close(LinkBox.Text);
}
