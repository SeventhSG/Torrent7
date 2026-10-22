using System.ComponentModel;
using Avalonia;
using Avalonia.Collections;
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
    private readonly UiState ui = UiState.Load();
    private DataGridCollectionView? view;
    private MainViewModel? vm;
    private Func<bool> hideOnClose = () => false;
    private Action quit = () => { };

    public MainWindow()
    {
        InitializeComponent();
        toasts = new WindowNotificationManager(this) { Position = NotificationPosition.BottomRight, MaxItems = 3, Margin = new Thickness(0, 0, 8, 36) };

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropOverlay.IsVisible = false);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Activated += OnActivated;
        timer.Tick += async (_, _) =>
        {
            if (vm != null) await vm.TickAsync();
        };

        foreach (var bar in new[] { TitleLeft, TitleRight })
        {
            bar.PointerPressed += OnTitlePressed;
            bar.DoubleTapped += OnTitleDoubleTapped;
        }
        Table.SelectionChanged += (_, _) => vm?.SetSelection(Table.SelectedItems.OfType<TorrentRowViewModel>().ToList());
        Table.DoubleTapped += (_, e) =>
        {
            if (e.Source is Control { DataContext: TorrentRowViewModel }) vm?.OpenFolderSelectedCommand.Execute(null);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && SearchBox.IsFocused)
            {
                SearchBox.Text = "";
                Table.Focus();
                e.Handled = true;
            }
        };

        RestorePlacement();
        SizeChanged += (_, e) => TitleRight.Classes.Set("compact", e.NewSize.Width < 1120);
        Opened += (_, _) =>
        {
            // Without extended decorations (most Linux desktops) the system draws its own title bar.
            if (!IsExtendedIntoWindowDecorations) CaptionSpace.Width = 0;
        };
    }

    public void Attach(MainViewModel model, Func<bool> hideOnClose, Action quit)
    {
        vm = model;
        DataContext = model;
        this.hideOnClose = hideOnClose;
        this.quit = quit;

        // The table sorts through its own view, so the order can be saved and restored.
        view = new DataGridCollectionView(model.Rows);
        Table.ItemsSource = view;
        // The grid selects its first row by itself; start with nothing selected and the details closed.
        Dispatcher.UIThread.Post(() => Table.SelectedItem = null, DispatcherPriority.Loaded);
        RestoreTable();
        // The details row takes no room while nothing is selected, and comes back at the height it was dragged to.
        CollapseDetails(!model.ShowDetails);
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ShowDetails)) CollapseDetails(!model.ShowDetails);
        };
        model.SelectRequested += row =>
        {
            if (row == null) return;
            Dispatcher.UIThread.Post(() =>
            {
                Table.SelectedItem = row;
                Table.ScrollIntoView(row, null);
            }, DispatcherPriority.Background);
        };
        timer.Start();
        StartTestHook();
    }

    /// <summary>
    /// For driving the interface in automated checks without taking over the mouse: with
    /// TORRENT7_UITEST set, each line written to uitest.cmd in the data folder is run once.
    /// Off, and absent from the process, in normal use.
    /// </summary>
    private void StartTestHook()
    {
        if (Environment.GetEnvironmentVariable("TORRENT7_UITEST") != "1" || vm == null) return;
        var file = Path.Combine(AppPaths.DataDir, "uitest.cmd");
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        poll.Tick += (_, _) =>
        {
            if (!File.Exists(file)) return;
            string[] lines;
            try { lines = File.ReadAllLines(file); File.Delete(file); }
            catch (IOException) { return; }
            foreach (var line in lines)
            {
                var (cmd, arg) = line.IndexOf(':') is var i and > 0 ? (line[..i], line[(i + 1)..]) : (line, "");
                switch (cmd)
                {
                    case "select":
                        var rows = vm.Rows.Where(r => arg.Split('|').Any(a => r.Name.StartsWith(a, StringComparison.OrdinalIgnoreCase))).ToList();
                        Table.SelectedItems.Clear();
                        foreach (var r in rows) Table.SelectedItems.Add(r);
                        break;
                    case "clear": Table.SelectedItems.Clear(); break;
                    case "sort":
                        var key = arg.TrimEnd('-');
                        var col = Table.Columns.First(c => ColumnKey(c) == key);
                        view?.SortDescriptions.Clear();
                        view?.SortDescriptions.Add(DataGridSortDescription.FromPath(col.SortMemberPath!, arg.EndsWith('-') ? ListSortDirection.Descending : ListSortDirection.Ascending));
                        break;
                    case "close": Table.ContextFlyout?.Hide(); (Table.Resources["ColumnsMenu"] as MenuFlyout)?.Hide(); break;
                    case "tab": vm.DetailTab = int.Parse(arg); break;
                    case "page": vm.ShowPage(Enum.Parse<Page>(arg)); break;
                    case "filter": vm.SelectedFilter = vm.Filters.First(f => f.Title.StartsWith(arg, StringComparison.OrdinalIgnoreCase)); break;
                    case "search": vm.Search = arg; break;
                    case "size": Width = double.Parse(arg.Split('x')[0]); Height = double.Parse(arg.Split('x')[1]); break;
                    case "addlink": _ = vm.AddLinkCommand.ExecuteAsync(null); break;
                    case "remove": _ = vm.RemoveSelectedCommand.ExecuteAsync(null); break;
                    case "columns": (Table.Resources["ColumnsMenu"] as MenuFlyout)?.ShowAt(Table); break;
                    case "menu": Table.ContextFlyout?.ShowAt(Table); break;
                }
            }
        };
        poll.Start();
        Log.Info("ui test hook on");
    }

    // ------------------------------------------------------------------ placement and layout

    private void RestorePlacement()
    {
        Width = Math.Max(MinWidth, ui.Width);
        Height = Math.Max(MinHeight, ui.Height);
        if (ui.X is { } x && ui.Y is { } y)
        {
            // Only where a screen still is: a monitor unplugged since last time would hide the window.
            var point = new PixelPoint((int)x, (int)y);
            if (Screens.All.Any(s => s.WorkingArea.Contains(point + new PixelPoint(40, 20))))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = point;
            }
        }
        if (ui.Maximized) WindowState = WindowState.Maximized;
        TransfersGrid.RowDefinitions[4].Height = new GridLength(Math.Clamp(ui.DetailsHeight, 150, 700));
    }

    private static string ColumnKey(DataGridColumn c) => c.Tag as string ?? c.Header as string ?? "";

    /// <summary>Columns that start hidden: useful, but not worth the room on a first look.</summary>
    private static readonly HashSet<string> HiddenByDefault = ["Ratio", "Added"];

    /// <summary>The columns the user wants, whether or not they fit right now.</summary>
    private readonly Dictionary<DataGridColumn, bool> wanted = [];

    /// <summary>When the table is too narrow, these give way first, in this order.</summary>
    private static readonly string[] GiveWay = ["Added", "Ratio", "Peers", "Time left", "Up", "Size"];

    private bool Wants(DataGridColumn c) => wanted.TryGetValue(c, out var w) ? w : c.IsVisible;

    private void SetWanted(DataGridColumn c, bool on)
    {
        wanted[c] = on;
        FitColumns();
    }

    /// <summary>Shows every wanted column that fits beside a readable name, dropping the least useful first.</summary>
    private void FitColumns()
    {
        var room = Table.Bounds.Width - 4;
        if (room <= 0) return;
        var name = Table.Columns.First(c => ColumnKey(c) == "Name");
        var show = Table.Columns.Where(c => c != name && Wants(c)).ToHashSet();
        double Need() => name.MinWidth + show.Sum(c => c.Width.IsAbsolute ? c.Width.Value : c.ActualWidth);
        foreach (var key in GiveWay)
        {
            if (Need() <= room) break;
            show.RemoveWhere(c => ColumnKey(c) == key);
        }
        var changed = false;
        foreach (var c in Table.Columns)
        {
            if (c == name || c.IsVisible == show.Contains(c)) continue;
            c.IsVisible = show.Contains(c);
            // A column shown again beside the stretching name column comes back zero wide unless its width is set again.
            if (c.IsVisible && c.Width.IsAbsolute) c.Width = new DataGridLength(c.Width.Value);
            changed = true;
        }
        if (changed) Table.InvalidateMeasure();
    }

    private void RestoreTable()
    {
        if (ui.Columns.Count == 0)
            foreach (var col in Table.Columns) wanted[col] = !HiddenByDefault.Contains(ColumnKey(col));

        foreach (var saved in ui.Columns.OrderBy(c => c.DisplayIndex))
        {
            var col = Table.Columns.FirstOrDefault(c => ColumnKey(c) == saved.Header);
            if (col == null) continue;
            if (saved.Width > 0 && !col.Width.IsStar) col.Width = new DataGridLength(saved.Width);
            if (saved.DisplayIndex >= 0 && saved.DisplayIndex < Table.Columns.Count) col.DisplayIndex = saved.DisplayIndex;
            wanted[col] = saved.Visible || ColumnKey(col) == "Name";
        }
        // After the resize has settled: changing columns in the middle of it leaves the grid half laid out.
        Table.SizeChanged += (_, _) => Dispatcher.UIThread.Post(FitColumns, DispatcherPriority.Background);
        FitColumns();

        Table.Resources["ColumnsMenu"] = BuildColumnsMenu();

        var sortCol = Table.Columns.FirstOrDefault(c => ColumnKey(c) == (ui.SortColumn ?? "Added"));
        if (sortCol?.SortMemberPath is { } path && view != null)
        {
            var dir = ui.SortColumn == null || ui.SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            view.SortDescriptions.Add(DataGridSortDescription.FromPath(path, dir));
        }
    }

    private void CollapseDetails(bool collapse)
    {
        var row = TransfersGrid.RowDefinitions[4];
        if (collapse)
        {
            if (row.ActualHeight > 0) ui.DetailsHeight = row.ActualHeight;
            row.Height = new GridLength(0);
        }
        else
        {
            row.Height = new GridLength(Math.Clamp(ui.DetailsHeight, 150, 700));
        }
    }

    private void SaveLayout()
    {
        if (WindowState == WindowState.Normal)
        {
            ui.X = Position.X;
            ui.Y = Position.Y;
            ui.Width = Width;
            ui.Height = Height;
        }
        ui.Maximized = WindowState == WindowState.Maximized;
        var details = TransfersGrid.RowDefinitions[4];
        if (details.ActualHeight > 0) ui.DetailsHeight = details.ActualHeight;
        ui.Columns = Table.Columns
            .Select(c => new ColumnState
            {
                Header = ColumnKey(c),
                Width = c.Width.IsAbsolute ? c.Width.Value : 0,
                DisplayIndex = c.DisplayIndex,
                Visible = Wants(c),
            })
            .ToList();
        if (view?.SortDescriptions.FirstOrDefault() is { } sort)
        {
            ui.SortColumn = Table.Columns.FirstOrDefault(c => c.SortMemberPath == sort.PropertyPath) is { } sc ? ColumnKey(sc) : null;
            ui.SortDescending = sort.Direction == ListSortDirection.Descending;
        }
        ui.Save();
    }

    /// <summary>Right click on a column header: tick the columns you want to see.</summary>
    private MenuFlyout BuildColumnsMenu()
    {
        var menu = new MenuFlyout();
        var checks = new List<(DataGridColumn Col, Controls.StrokeIcon Tick)>();
        foreach (var col in Table.Columns.OrderBy(c => c.DisplayIndex))
        {
            if (ColumnKey(col) == "Name") continue;
            var tick = new Controls.StrokeIcon { Data = this.FindResource("IconCheck") as Avalonia.Media.Geometry };
            var item = new MenuItem { Header = ColumnKey(col), Icon = tick };
            item.Click += (_, _) => SetWanted(col, !Wants(col));
            checks.Add((col, tick));
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var reset = new MenuItem { Header = "Reset columns" };
        reset.Click += (_, _) =>
        {
            foreach (var col in Table.Columns) wanted[col] = !HiddenByDefault.Contains(ColumnKey(col));
            FitColumns();
        };
        menu.Items.Add(reset);
        // The ticks follow the columns each time the menu opens.
        menu.Opening += (_, _) =>
        {
            foreach (var (col, tick) in checks) tick.IsVisible = Wants(col);
        };
        return menu;
    }

    // ------------------------------------------------------------------ title bar

    private void OnTitlePressed(object? sender, PointerPressedEventArgs e)
    {
        // Drag from the bar itself, never from a button or the search box on it.
        if (e.Source is Button or TextBox || (e.Source as Visual)?.FindAncestorOfType<Button>() != null) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.ClickCount == 1) BeginMoveDrag(e);
    }

    private void OnTitleDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Button or TextBox || (e.Source as Visual)?.FindAncestorOfType<Button>() != null) return;
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    // ------------------------------------------------------------------ lifetime

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (vm == null) return;
        SaveLayout();
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
        SaveLayout();
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
        foreach (var f in e.DataTransfer.TryGetFiles() ?? [])
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

    // ------------------------------------------------------------------ IShell

    public async Task<IReadOnlyList<string>> PickTorrentFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open torrent files",
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

    public async Task<(bool Ok, bool DeleteFiles)> ConfirmRemoveAsync(string what, int count, bool canDeleteFiles)
    {
        var dialog = new RemoveDialog(what, count, canDeleteFiles);
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

    public Task<string?> ShowAddLinkAsync(string? suggestion) =>
        new AddLinkWindow(suggestion).ShowDialog<string?>(this);

    public async Task<string?> GetClipboardTextAsync()
    {
        try { return Clipboard == null ? null : await Clipboard.TryGetTextAsync(); }
        catch (Exception ex)
        {
            Log.Warn("clipboard: " + ex.Message);
            return null;
        }
    }

    public async Task OpenAsync(string path)
    {
        var ok = false;
        try
        {
            if (Directory.Exists(path))
                ok = await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
            else if (File.Exists(path))
                ok = await Launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
        catch (Exception ex) { Log.Warn($"open {path}: {ex.Message}"); }
        if (!ok) Notify("Could not open it", path, error: true);
    }

    public async Task OpenUrlAsync(string url)
    {
        try { await Launcher.LaunchUriAsync(new Uri(url)); }
        catch (Exception ex) { Log.Warn("open url: " + ex.Message); }
    }

    public async Task CopyAsync(string text)
    {
        if (Clipboard != null) await Clipboard.SetTextAsync(text);
    }

    public void Notify(string title, string message, bool error = false) =>
        toasts.Show(new Notification(title, message, error ? NotificationType.Error : NotificationType.Information, TimeSpan.FromSeconds(error ? 8 : 4)));
}
