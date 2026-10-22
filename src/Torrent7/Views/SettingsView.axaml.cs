using Avalonia;
using Avalonia.Controls;

namespace Torrent7.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        // The index on the left scrolls the page to its section.
        Sections.SelectionChanged += (_, _) =>
        {
            if (Sections.SelectedItem is ListBoxItem { Tag: string name } && this.FindControl<Control>(name) is { } target)
                Scroller.Offset = Scroller.Offset.WithY(Math.Max(0, target.TranslatePoint(new Avalonia.Point(0, 0), (Control)Scroller.Content!)?.Y - 8 ?? 0));
        };
    }
}
