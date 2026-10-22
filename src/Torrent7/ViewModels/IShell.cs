using Torrent7.Core;

namespace Torrent7.ViewModels;

/// <summary>What the view models need from the window: pickers, dialogs, the desktop.</summary>
public interface IShell
{
    Task<IReadOnlyList<string>> PickTorrentFilesAsync();
    Task<string?> PickFolderAsync(string? start);
    Task<(bool Ok, bool DeleteFiles)> ConfirmRemoveAsync(string name, bool canDeleteFiles);
    Task<AddRequest?> ShowAddDialogAsync(AddTorrentViewModel vm);
    Task OpenAsync(string path);
    Task CopyAsync(string text);
    void Notify(string title, string message, bool error = false);
    void BringToFront();
}
