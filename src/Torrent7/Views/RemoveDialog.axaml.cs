using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Torrent7.Views;

/// <summary>Asks before removing, and makes deleting files a separate, red, explicit choice.</summary>
public sealed partial class RemoveDialog : Window
{
    public RemoveDialog() : this("", 1, false) { }

    public RemoveDialog(string what, int count, bool canDeleteFiles)
    {
        InitializeComponent();
        Heading.Text = count == 1 ? "Remove this torrent?" : $"Remove {count} torrents?";
        NameText.Text = count == 1 ? what : "They come off the list. Their files stay where they are unless you tick the box.";
        DeleteRow.IsVisible = canDeleteFiles;
        DeleteBox.IsCheckedChanged += (_, _) =>
        {
            var on = DeleteBox.IsChecked == true;
            DeleteWarning.IsVisible = on;
            RemoveButton.Content = on ? "Remove and delete files" : "Remove";
            RemoveButton.Classes.Set("danger", on);
        };
    }

    public bool DeleteFiles => DeleteBox.IsChecked == true;

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
    private void OnRemove(object? sender, RoutedEventArgs e) => Close(true);
}
