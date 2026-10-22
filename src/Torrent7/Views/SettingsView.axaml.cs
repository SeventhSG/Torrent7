using Avalonia.Controls;

namespace Torrent7.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        // Each section is its own page: the index on the left shows one, the rest stay out of the way.
        Sections.SelectionChanged += (_, _) => ShowSection((Sections.SelectedItem as ListBoxItem)?.Tag as string);
        Sections.SelectedIndex = 0;
    }

    private void ShowSection(string? name)
    {
        if (name == null) return;
        var visible = false;
        foreach (var child in Pages.Children)
        {
            // A heading starts a section; everything until the next heading belongs to it.
            if (child is TextBlock { Name: { Length: > 0 } heading } && child.Classes.Contains("group"))
                visible = heading == name;
            child.IsVisible = visible;
        }
        Scroller.Offset = default;
    }
}
