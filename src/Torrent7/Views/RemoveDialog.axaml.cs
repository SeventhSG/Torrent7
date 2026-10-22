using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Torrent7.Views;

/// <summary>Asks before removing, and makes deleting files a separate, red, explicit choice.</summary>
public sealed partial class RemoveDialog : Window
{
    public RemoveDialog() : this("", false) { }

    public RemoveDialog(string name, bool canDeleteFiles)
    {
        InitializeComponent();
        NameText.Text = name;
        DeleteBox.IsVisible = canDeleteFiles;
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
