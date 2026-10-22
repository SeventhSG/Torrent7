using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoTorrent;
using Torrent7.Core;

namespace Torrent7.ViewModels;

public enum Page { Transfers, SpeedTest, Settings }
public enum Filter { All, Downloading, Seeding, Completed, Paused, Errors }

/// <summary>
/// The main window: the sidebar, the torrent table and what is selected in it, the toolbar
/// and the status bar. One timer ticks the service and then refreshes what is on screen.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly TorrentService service;
    private readonly HttpClient http;
    private readonly Dictionary<string, TorrentRowViewModel> rowsById = [];
    private readonly HashSet<string> dismissedClipboard = [];
    private IReadOnlyList<TorrentRowViewModel> selection = [];
    private FileSystemWatcher? watcher;

    public MainViewModel(TorrentService service, HttpClient http, IShell shell)
    {
        this.service = service;
        this.http = http;
        Shell = shell;
        Settings = new SettingsViewModel(service, shell, () => { RefreshTop(); StartWatchFolder(); });
        SpeedTest = new SpeedTestViewModel(service, http, shell, () => { Settings.Reload(); RefreshTop(); return Task.CompletedTask; });

        Filters =
        [
            new NavItem("All transfers", "IconStack", Filter.All),
            new NavItem("Downloading", "IconCircleDown", Filter.Downloading),
            new NavItem("Seeding", "IconCircleUp", Filter.Seeding),
            new NavItem("Completed", "IconCircleCheck", Filter.Completed),
            new NavItem("Paused", "IconCirclePause", Filter.Paused),
            new NavItem("Errors", "IconAlert", Filter.Errors, danger: true),
        ];
        Tools =
        [
            new NavItem("Speed test", "IconGauge", page: ViewModels.Page.SpeedTest),
            new NavItem("Settings", "IconSettings", page: ViewModels.Page.Settings),
        ];
        selectedFilter = Filters[0];

        service.Added += e => Ui(() => AddRow(e));
        service.Removed += e => Ui(() => RemoveRow(e));
        service.Completed += e => Ui(() =>
        {
            if (service.Settings.ScanDownloads) _ = ScanAsync(e);
            else if (service.Settings.NotifyOnComplete) Shell.Notify("Download finished", e.Name);
        });
        service.Problem += msg => Ui(() => Shell.Notify("Something went wrong", msg, error: true));

        foreach (var e in service.Entries) AddRow(e);
        StartWatchFolder();
        RefreshTop();
        Rebuild();
    }

    public IShell Shell { get; }
    public SpeedTestViewModel SpeedTest { get; }
    public SettingsViewModel Settings { get; }
    public SpeedHistory History => service.History;
    public ObservableCollection<NavItem> Filters { get; }
    public ObservableCollection<NavItem> Tools { get; }

    /// <summary>Marshals onto the UI thread. Set by the app; defaults to running inline for tests.</summary>
    public static Action<Action> Ui { get; set; } = a => a();

    /// <summary>The table's rows: every torrent that passes the current filter and search.</summary>
    public ObservableCollection<TorrentRowViewModel> Rows { get; } = [];

    [ObservableProperty] private Page page = Page.Transfers;
    [ObservableProperty] private NavItem? selectedFilter;
    [ObservableProperty] private NavItem? selectedTool;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private TorrentRowViewModel? selected;
    [ObservableProperty] private int selectionCount;
    [ObservableProperty] private bool isAdding;

    [ObservableProperty] private string downRateText = "0 B/s";
    [ObservableProperty] private string upRateText = "0 B/s";
    [ObservableProperty] private string limitText = "";
    [ObservableProperty] private bool slowMode;
    [ObservableProperty] private string slowText = "";
    [ObservableProperty] private int historyVersion;

    [ObservableProperty] private string dhtText = "";
    [ObservableProperty] private string portText = "";
    [ObservableProperty] private bool portOk;
    [ObservableProperty] private string connectionsText = "";
    [ObservableProperty] private string freeText = "";
    [ObservableProperty] private string totalsText = "";
    [ObservableProperty] private int countAll;

    [ObservableProperty] private string? updateText;
    [ObservableProperty] private string? clipboardOffer;
    [ObservableProperty] private string clipboardOfferText = "";

    public Filter Filter => SelectedFilter?.Filter ?? Filter.All;
    public bool IsTransfers => Page == Page.Transfers;
    public bool IsSpeedTest => Page == Page.SpeedTest;
    public bool IsSettings => Page == Page.Settings;
    public bool IsEmpty => CountAll == 0;
    public bool NothingMatches => CountAll > 0 && Rows.Count == 0;

    /// <summary>A short list leaves room: use it to say how to add the next one.</summary>
    public bool ShowDropHint => Rows.Count is > 0 and < 8 && !ShowDetails;
    public bool HasSelection => SelectionCount > 0;
    public bool ShowDetails => Selected != null;
    public string ListTitle => Search.Trim().Length > 0 ? $"Results for \"{Search.Trim()}\"" : SelectedFilter?.Title ?? "All transfers";
    public string SelectionText => SelectionCount > 1 ? $"{SelectionCount} selected" : "";

    partial void OnPageChanged(Page value)
    {
        OnPropertyChanged(nameof(IsTransfers));
        OnPropertyChanged(nameof(IsSpeedTest));
        OnPropertyChanged(nameof(IsSettings));
        if (value == Page.Settings) Settings.Reload();
        if (value == Page.SpeedTest) SpeedTest.ShowPrevious();
    }

    partial void OnSelectedFilterChanged(NavItem? value)
    {
        if (value == null) return;
        SelectedTool = null;
        Page = Page.Transfers;
        OnPropertyChanged(nameof(Filter));
        OnPropertyChanged(nameof(ListTitle));
        Rebuild();
    }

    partial void OnSelectedToolChanged(NavItem? value)
    {
        if (value?.Page is not { } p) return;
        SelectedFilter = null;
        Page = p;
    }

    partial void OnSearchChanged(string value)
    {
        if (Page != Page.Transfers) SelectedFilter = Filters[0];
        OnPropertyChanged(nameof(ListTitle));
        Rebuild();
    }

    partial void OnCountAllChanged(int value) => OnPropertyChanged(nameof(IsEmpty));

    partial void OnSelectedChanged(TorrentRowViewModel? value)
    {
        OnPropertyChanged(nameof(ShowDetails));
        OnPropertyChanged(nameof(ShowDropHint));
        LoadDetails(value);
    }

    partial void OnSelectionCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionText));
        foreach (var c in new IRelayCommand[] { ResumeSelectedCommand, PauseSelectedCommand, RemoveSelectedCommand, OpenFolderSelectedCommand, ForceStartSelectedCommand, RecheckSelectedCommand, CopyMagnetSelectedCommand })
            c.NotifyCanExecuteChanged();
    }

    /// <summary>Called by the table whenever its selection changes.</summary>
    public void SetSelection(IReadOnlyList<TorrentRowViewModel> rows)
    {
        selection = rows;
        SelectionCount = rows.Count;
        Selected = rows.Count == 1 ? rows[0] : null;
    }

    public void ShowPage(Page p)
    {
        if (p == Page.Transfers)
        {
            SelectedFilter ??= Filters[0];
            Page = p;
        }
        else SelectedTool = Tools.First(t => t.Page == p);
    }

    partial void OnSlowModeChanged(bool value)
    {
        if (service.Settings.SlowMode == value) return;
        var s = service.Settings.Clone();
        s.SlowMode = value;
        _ = ApplyAndRefreshAsync(s);
    }

    private async Task ApplyAndRefreshAsync(AppSettings s)
    {
        await service.ApplySettingsAsync(s);
        await service.TickAsync();
        RefreshTop();
    }

    // ------------------------------------------------------------------ the tick

    public async Task TickAsync()
    {
        await service.TickAsync();
        foreach (var row in rowsById.Values) row.Refresh();
        RefreshTop();
        RefreshDetails();
        Rebuild();
    }

    private void RefreshTop()
    {
        var s = service.Settings;
        DownRateText = Format.Speed(service.DownRate);
        UpRateText = Format.Speed(service.UpRate);
        HistoryVersion++;

        SlowMode = s.SlowMode;
        var (down, up) = SpeedSchedule.EffectiveLimits(s, DateTime.Now);
        LimitText = down == 0 && up == 0 ? "No limits"
                  : $"Limited to {(down > 0 ? Format.Speed(down) : "any")} down, {(up > 0 ? Format.Speed(up) : "any")} up";
        SlowText = s.SlowMode ? "On until you turn it off"
                 : service.SlowActive ? $"On until {Format.ClockMinute(s.ScheduleToMinute)}"
                 : s.ScheduleEnabled ? $"Turns on at {Format.ClockMinute(s.ScheduleFromMinute)}"
                 : $"{Format.Limit(s.SlowDownloadLimitKiB)} down, {Format.Limit(s.SlowUploadLimitKiB)} up";

        DhtText = !s.Dht ? "DHT off" : service.DhtNodes > 0 ? $"DHT {service.DhtNodes} nodes" : "DHT connecting";
        PortOk = service.PortMapped;
        PortText = $"Port {s.ListenPort}" + (s.PortForwarding ? (service.PortMapped ? " open" : " not forwarded") : "");
        ConnectionsText = $"{service.OpenConnections} peers";
        var free = AddTorrentViewModel.FreeSpace(s.DownloadDir);
        FreeText = free is { } f ? $"{Format.Size(f)} free" : "";

        var entries = service.Entries;
        TotalsText = $"{Format.Size(entries.Sum(e => e.Record.Downloaded))} down, {Format.Size(entries.Sum(e => e.Record.Uploaded))} up in total";
    }

    // ------------------------------------------------------------------ the list

    private void AddRow(TorrentEntry e)
    {
        if (rowsById.ContainsKey(e.Id)) return;
        var row = new TorrentRowViewModel(e);
        row.Refresh();
        rowsById[e.Id] = row;
        Rebuild();
    }

    private void RemoveRow(TorrentEntry e)
    {
        if (!rowsById.Remove(e.Id, out var row)) return;
        if (Selected == row) Selected = null;
        Rebuild();
    }

    private static bool Matches(TorrentRowViewModel r, Filter f) => f switch
    {
        Filter.Downloading => r.Status is EntryStatus.Downloading or EntryStatus.Metadata or EntryStatus.Starting
                                       or EntryStatus.Queued or EntryStatus.Checking or EntryStatus.Moving,
        Filter.Seeding => r.Status == EntryStatus.Seeding,
        Filter.Completed => r.Status is EntryStatus.Done or EntryStatus.Seeding,
        Filter.Paused => r.Status == EntryStatus.Paused,
        Filter.Errors => r.Status == EntryStatus.Error,
        _ => true,
    };

    /// <summary>Brings the table in line with the filter and search, touching as little as possible.</summary>
    private void Rebuild()
    {
        var all = rowsById.Values.OrderByDescending(r => r.Entry.Record.AddedUtc).ToList();
        CountAll = all.Count;
        foreach (var n in Filters)
            n.Count = all.Count(r => Matches(r, n.Filter!.Value));
        var errors = Filters.First(n => n.Filter == Filter.Errors);
        errors.IsShown = errors.Count > 0;

        var term = Search.Trim();
        var wanted = all.Where(r => Matches(r, Filter) &&
                                    (term.Length == 0 || r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
        var set = wanted.ToHashSet();

        for (var i = Rows.Count - 1; i >= 0; i--)
            if (!set.Contains(Rows[i])) Rows.RemoveAt(i);
        // The table keeps its own sort order, so only membership matters here.
        foreach (var r in wanted)
            if (!Rows.Contains(r)) Rows.Add(r);
        OnPropertyChanged(nameof(NothingMatches));
        OnPropertyChanged(nameof(ShowDropHint));
    }

    // ------------------------------------------------------------------ adding

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        foreach (var path in await Shell.PickTorrentFilesAsync())
            await AddSourceAsync(path);
    }

    [RelayCommand]
    private async Task AddLinkAsync()
    {
        var clip = await Shell.GetClipboardTextAsync();
        var suggestion = TorrentInput.Classify(clip).Kind is TorrentInputKind.Magnet or TorrentInputKind.InfoHash or TorrentInputKind.Url ? clip!.Trim() : null;
        var text = await Shell.ShowAddLinkAsync(suggestion);
        if (string.IsNullOrWhiteSpace(text)) return;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            await AddSourceAsync(line);
    }

    /// <summary>Ctrl+V anywhere outside a text box: whatever torrents are on the clipboard.</summary>
    [RelayCommand]
    private async Task PasteAsync()
    {
        var clip = await Shell.GetClipboardTextAsync();
        var lines = (clip ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Where(l => TorrentInput.Classify(l).Kind != TorrentInputKind.None).ToList();
        if (lines.Count == 0)
        {
            Shell.Notify("Nothing to add", "The clipboard has no magnet link, info hash or torrent link on it.");
            return;
        }
        foreach (var line in lines) await AddSourceAsync(line);
    }

    /// <summary>Anything: a magnet, a hash, a link to a .torrent, or a file. Used by paste, drop, the command line and the watch folder.</summary>
    public async Task<bool> AddSourceAsync(string text, bool quiet = false)
    {
        var (kind, value) = TorrentInput.Classify(text);
        if (kind == TorrentInputKind.None)
        {
            Shell.Notify("Not a torrent", "Use a magnet link, an info hash, or a link to a .torrent file.", error: true);
            return false;
        }

        IsAdding = true;
        try
        {
            byte[]? bytes = null;
            MagnetLink? magnet = null;
            if (kind is TorrentInputKind.Magnet or TorrentInputKind.InfoHash)
            {
                if (!MagnetLink.TryParse(value, out magnet))
                {
                    Shell.Notify("That magnet link is broken", "Its info hash is missing or damaged.", error: true);
                    return false;
                }
                if (Existing(magnet.InfoHashes)) return true;
            }
            else
            {
                bytes = await service.FetchTorrentAsync(value);
                if (Existing(Torrent.Load(bytes).InfoHashes)) return true;
            }

            AddRequest? request;
            if (service.Settings.ShowAddDialog && !quiet)
            {
                request = await Shell.ShowAddDialogAsync(new AddTorrentViewModel(service, Shell, bytes, magnet));
                if (request == null) return true;
            }
            else
            {
                request = new AddRequest(bytes, magnet, service.Settings.DownloadDir, service.Settings.StartImmediately);
            }

            var entry = await service.AddAsync(request);
            ShowPage(Page.Transfers);
            await service.TickAsync();
            SelectRequested?.Invoke(rowsById.GetValueOrDefault(entry.Id));
            return true;
        }
        catch (DuplicateTorrentException dup)
        {
            Reveal(dup.Existing);
            Shell.Notify("Already in your list", dup.Existing.Name);
            return true;
        }
        catch (Exception ex)
        {
            Shell.Notify("Could not add the torrent", ex.Message, error: true);
            return false;
        }
        finally
        {
            IsAdding = false;
        }
    }

    /// <summary>Asks the table to select a row and scroll to it.</summary>
    public event Action<TorrentRowViewModel?>? SelectRequested;

    private bool Existing(InfoHashes hashes)
    {
        if (service.Find(hashes) is not { } e) return false;
        Reveal(e);
        Shell.Notify("Already in your list", e.Name);
        return true;
    }

    private void Reveal(TorrentEntry e)
    {
        Search = "";
        SelectedFilter = Filters[0];
        SelectRequested?.Invoke(rowsById.GetValueOrDefault(e.Id));
    }

    /// <summary>Called when the window comes to the front. Offers a magnet the user just copied.</summary>
    public void OfferClipboard(string? text)
    {
        if (!service.Settings.WatchClipboard || !TorrentInput.IsWorthOffering(text)) return;
        var (_, value) = TorrentInput.Classify(text);
        if (dismissedClipboard.Contains(value) || !MagnetLink.TryParse(value, out var magnet)) return;
        if (service.Find(magnet.InfoHashes) != null) return;
        ClipboardOffer = value;
        ClipboardOfferText = string.IsNullOrEmpty(magnet.Name) ? "You copied a magnet link." : $"You copied \"{magnet.Name}\".";
    }

    [RelayCommand]
    private async Task AcceptClipboardAsync()
    {
        var value = ClipboardOffer;
        DismissClipboard();
        if (value != null) await AddSourceAsync(value);
    }

    [RelayCommand]
    private void DismissClipboard()
    {
        if (ClipboardOffer != null) dismissedClipboard.Add(ClipboardOffer);
        ClipboardOffer = null;
    }

    // ------------------------------------------------------------------ updates

    public async Task CheckForUpdateAsync()
    {
        var current = typeof(MainViewModel).Assembly.GetName().Version ?? new Version(0, 0);
        if (await UpdateCheck.NewerAsync(http, new Version(current.Major, current.Minor, current.Build)) is { } v)
            UpdateText = $"Update to {v} available";
    }

    /// <summary>Opens the release page; the installer there updates this copy in place.</summary>
    [RelayCommand]
    private Task OpenUpdateAsync() => Shell.OpenUrlAsync(UpdateCheck.ReleasesPage);

    // ------------------------------------------------------------------ virus scan

    /// <summary>Scans a finished download, then says so. A threat is always shown, notifications or not.</summary>
    private async Task ScanAsync(TorrentEntry e)
    {
        var path = e.HasMetadata && e.Manager.Files.Count > 1 ? e.Manager.ContainingDirectory : e.Manager.Files.FirstOrDefault()?.FullPath ?? e.Record.SaveDir;
        e.Scanning = true;
        try
        {
            var result = await Safety.ScanLocalAsync(path);
            if (result.Verdict != ScanVerdict.Threat && !string.IsNullOrWhiteSpace(service.Settings.VirusTotalKey))
            {
                var vt = await Safety.CheckVirusTotalAsync(http, service.Settings.VirusTotalKey!, e.Manager.Files.Select(f => f.FullPath).Where(File.Exists));
                if (vt.Verdict == ScanVerdict.Threat) result = vt;
            }
            e.Record.ScanVerdict = result.Verdict;
            e.Record.ScanMessage = result.Message;
            service.SaveSession();
            Log.Info($"scan {e.Name}: {result.Verdict} {result.Message}");
            if (result.Verdict == ScanVerdict.Threat) Shell.Notify("Threat found in " + e.Name, result.Message, error: true);
            else if (service.Settings.NotifyOnComplete)
                Shell.Notify("Download finished", result.Verdict == ScanVerdict.Clean ? $"{e.Name}. Scanned, nothing found." : $"{e.Name}. {result.Message}");
        }
        catch (Exception ex) { Log.Error("scan " + e.Name, ex); }
        finally { e.Scanning = false; }
    }

    // ------------------------------------------------------------------ watch folder

    private void StartWatchFolder()
    {
        watcher?.Dispose();
        watcher = null;
        var dir = service.Settings.WatchFolder;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;

        watcher = new FileSystemWatcher(dir, "*.torrent") { EnableRaisingEvents = true };
        watcher.Created += (_, e) => Ui(() => _ = TakeFromWatchFolderAsync(e.FullPath));
        watcher.Renamed += (_, e) => Ui(() => _ = TakeFromWatchFolderAsync(e.FullPath));
        foreach (var f in Directory.EnumerateFiles(dir, "*.torrent"))
            _ = TakeFromWatchFolderAsync(f);
    }

    /// <summary>Adds the file with the default settings and renames it, so it is never added twice.</summary>
    private async Task TakeFromWatchFolderAsync(string path)
    {
        if (!path.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)) return;
        await Task.Delay(500); // let whatever is writing it finish
        if (!File.Exists(path)) return;
        if (await AddSourceAsync(path, quiet: true))
        {
            try { File.Move(path, path + ".added", overwrite: true); }
            catch (IOException ex) { Log.Warn($"watch folder rename {path}: {ex.Message}"); }
        }
    }

    // ------------------------------------------------------------------ actions on the selection

    private bool CanAct() => SelectionCount > 0;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ResumeSelectedAsync()
    {
        var rows = selection.ToList();
        foreach (var r in rows) r.Entry.Record.WantsToRun = true;
        RefreshRows();
        foreach (var r in rows) await service.ResumeAsync(r.Entry);
        RefreshRows();
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task PauseSelectedAsync()
    {
        var rows = selection.ToList();
        // Show the intent straight away; the peers are let go in the background, all at once.
        foreach (var r in rows) r.Entry.Record.WantsToRun = false;
        RefreshRows();
        await Task.WhenAll(rows.Select(r => service.PauseAsync(r.Entry)));
        RefreshRows();
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ForceStartSelectedAsync()
    {
        foreach (var r in selection.ToList()) await service.ResumeAsync(r.Entry, force: true);
        RefreshRows();
    }

    /// <summary>Space in the table: pause what is running, resume what is not.</summary>
    [RelayCommand]
    private async Task TogglePauseSelectedAsync()
    {
        if (selection.Count == 0) return;
        if (selection.Any(r => r.Entry.Record.WantsToRun)) await PauseSelectedAsync();
        else await ResumeSelectedAsync();
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task OpenFolderSelectedAsync()
    {
        var e = selection[0].Entry;
        var path = e.HasMetadata && e.Manager.Files.Count > 1 ? e.Manager.ContainingDirectory : e.Record.SaveDir;
        if (!Directory.Exists(path)) path = e.Record.SaveDir;
        await Shell.OpenAsync(path);
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task RemoveSelectedAsync()
    {
        var rows = selection.ToList();
        if (rows.Count == 0) return;
        var what = rows.Count == 1 ? rows[0].Name : $"{rows.Count} torrents";
        var (ok, deleteFiles) = await Shell.ConfirmRemoveAsync(what, rows.Count, rows.Any(r => r.Entry.HasMetadata));
        if (!ok) return;
        foreach (var r in rows)
        {
            try { await service.RemoveAsync(r.Entry, deleteFiles); }
            catch (Exception ex) { Shell.Notify("Could not remove " + r.Name, ex.Message, error: true); }
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task RecheckSelectedAsync()
    {
        foreach (var r in selection.ToList())
        {
            try { await service.RecheckAsync(r.Entry); }
            catch (Exception ex) { Shell.Notify("Could not check " + r.Name, ex.Message, error: true); }
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task CopyMagnetSelectedAsync()
    {
        var links = selection.Select(r => MagnetFor(r.Entry)).Where(l => l.Length > 0).ToList();
        await Shell.CopyAsync(string.Join(Environment.NewLine, links));
        Shell.Notify(links.Count == 1 ? "Magnet link copied" : $"{links.Count} magnet links copied", links.Count == 1 ? selection[0].Name : "One per line.");
    }

    private static string MagnetFor(TorrentEntry e) =>
        e.Manager.Torrent is { } t
            ? new MagnetLink(t.InfoHashes, t.Name, t.AnnounceUrls.SelectMany(x => x).ToList(), null, t.Size).ToV1String()
            : e.Record.Magnet ?? "";

    [RelayCommand]
    private async Task ToggleSequentialAsync()
    {
        if (Selected is not { } row) return;
        await service.SetSequentialAsync(row.Entry, !row.Entry.Record.Sequential);
        row.Refresh();
        LoadDetails(row);
    }

    [RelayCommand]
    private async Task MoveSelectedAsync()
    {
        if (Selected is not { } row) return;
        var dir = await Shell.PickFolderAsync(row.Entry.Record.SaveDir);
        if (dir == null || string.Equals(Path.GetFullPath(dir), Path.GetFullPath(row.Entry.Record.SaveDir), StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            await service.MoveAsync(row.Entry, dir);
            Shell.Notify("Moved", $"{row.Name} is now in {dir}");
        }
        catch (Exception ex) { Shell.Notify("Could not move the files", ex.Message, error: true); }
        LoadDetails(Selected);
    }

    [RelayCommand] private Task PauseAllAsync() => service.PauseAllAsync();
    [RelayCommand] private Task ResumeAllAsync() => service.ResumeAllAsync();
    [RelayCommand] private void ToggleSlowMode() => SlowMode = !SlowMode;

    private void RefreshRows()
    {
        foreach (var r in selection) r.Refresh();
        Rebuild();
    }
}
