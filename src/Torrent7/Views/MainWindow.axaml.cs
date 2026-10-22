using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Torrent7.Core;
using Torrent7.ViewModels;

namespace Torrent7.Views;

public sealed partial class MainWindow : Window, IShell
{
    private readonly WindowNotificationManager toasts;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private MainViewModel? vm;
    private Func<bool> hideOnClose = () => false;
    private Action quit = () => { };

    public MainWindow()
    {
        InitializeComponent();
        toasts = new WindowNotificationManager(this) { Position = NotificationPosition.BottomRight, MaxItems = 3 };

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropOverlay.IsVisible = false);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Activated += OnActivated;
        timer.Tick += async (_, _) =>
        {
            if (vm != null) await vm.TickAsync();
        };
    }

    public void Attach(MainViewModel model, Func<bool> hideOnClose, Action quit)
    {
        vm = model;
        DataContext = model;
        this.hideOnClose = hideOnClose;
        this.quit = quit;
        timer.Start();
    }

    // ------------------------------------------------------------------ lifetime

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (vm == null) return;
        e.Cancel = true;
        if (hideOnClose())
        {
            Hide();
            Log.Info("hidden to tray");
        }
        else
        {
            quit();
        }
    }

    public void BringToFront()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    public void ShowFatal(string title, string text)
    {
        CurtainTitle.Text = title;
        CurtainText.Text = text;
        Curtain.IsVisible = true;
    }

    public void ShowClosing()
    {
        timer.Stop();
        ShowFatal("Closing", "Saving where every torrent got to, so the next start is instant.");
    }

    private async void OnActivated(object? sender, EventArgs e)
    {
        if (vm == null || Clipboard == null) return;
        try { vm.OfferClipboard(await Clipboard.TryGetTextAsync()); }
        catch (Exception ex) { Log.Warn("clipboard: " + ex.Message); }
    }

    // ------------------------------------------------------------------ drag and drop

    private static IEnumerable<string> DroppedTorrents(DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles() ?? [];
        foreach (var f in files)
        {
            var path = f.TryGetLocalPath();
            if (path != null && path.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)) yield return path;
        }
        if (e.DataTransfer.TryGetText() is { } text && TorrentInput.Classify(text).Kind != TorrentInputKind.None)
            yield return text;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var ok = vm != null && DroppedTorrents(e).Any();
        e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.IsVisible = ok;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        if (vm == null) return;
        foreach (var item in DroppedTorrents(e).ToList())
            await vm.AddSourceAsync(item);
    }

    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control button) return;
        var row = button.FindAncestorOfType<Border>();
        while (row != null && row.ContextFlyout == null) row = row.FindAncestorOfType<Border>();
        row?.ContextFlyout?.ShowAt(button);
    }

    // ------------------------------------------------------------------ IShell

    public async Task<IReadOnlyList<string>> PickTorrentFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open .torrent files",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Torrent files") { Patterns = ["*.torrent"], MimeTypes = ["application/x-bittorrent"] },
                FilePickerFileTypes.All,
            ],
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> PickFolderAsync(string? start)
    {
        IStorageFolder? startFolder = null;
        if (!string.IsNullOrEmpty(start) && Directory.Exists(start))
            startFolder = await StorageProvider.TryGetFolderFromPathAsync(start);
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder",
            SuggestedStartLocation = startFolder,
        });
        return picked.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<(bool Ok, bool DeleteFiles)> ConfirmRemoveAsync(string name, bool canDeleteFiles)
    {
        var dialog = new RemoveDialog(name, canDeleteFiles);
        var ok = await dialog.ShowDialog<bool>(this);
        return (ok, ok && dialog.DeleteFiles);
    }

    public async Task<AddRequest?> ShowAddDialogAsync(AddTorrentViewModel model)
    {
        BringToFront();
        var dialog = new AddTorrentWindow { DataContext = model };
        await dialog.ShowDialog(this);
        return model.Result;
    }

    public async Task OpenAsync(string path)
    {
        var launcher = Launcher;
        var ok = false;
        try
        {
            if (Directory.Exists(path))
                ok = await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
            else if (File.Exists(path))
                ok = await launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
        catch (Exception ex) { Log.Warn($"open {path}: {ex.Message}"); }
        if (!ok) Notify("Could not open it", path, error: true);
    }

    public async Task CopyAsync(string text)
    {
        if (Clipboard != null) await Clipboard.SetTextAsync(text);
    }

    public void Notify(string title, string message, bool error = false) =>
        toasts.Show(new Notification(title, message, error ? NotificationType.Error : NotificationType.Information, TimeSpan.FromSeconds(error ? 8 : 4)));
}
