using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoTorrent;
using Torrent7.Core;

namespace Torrent7.ViewModels;

public enum Page { Transfers, SpeedTest, Settings }
public enum Filter { All, Active, Seeding, Paused, Done, Errors }

/// <summary>
/// The main window: the list, its filters, the selected torrent's details, and the bar on
/// top. Runs off one timer that ticks the service and then refreshes what is on screen.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly TorrentService service;
    private readonly HttpClient http;
    private readonly Dictionary<string, TorrentRowViewModel> rowsById = [];
    private readonly HashSet<string> dismissedClipboard = [];
    private FileSystemWatcher? watcher;

    public MainViewModel(TorrentService service, HttpClient http, IShell shell)
    {
        this.service = service;
        this.http = http;
        Shell = shell;
        Settings = new SettingsViewModel(service, shell, () => { RefreshTop(); StartWatchFolder(); });
        SpeedTest = new SpeedTestViewModel(service, http, shell, () => { Settings.Reload(); RefreshTop(); return Task.CompletedTask; });

        service.Added += e => Ui(() => AddRow(e));
        service.Removed += e => Ui(() => RemoveRow(e));
        service.Completed += e => Ui(() =>
        {
            if (service.Settings.NotifyOnComplete) Shell.Notify("Download finished", e.Name);
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

    /// <summary>Marshals onto the UI thread. Set by the app; defaults to running inline for tests.</summary>
    public static Action<Action> Ui { get; set; } = a => a();

    public ObservableCollection<TorrentRowViewModel> Rows { get; } = [];

    [ObservableProperty] private Page page = Page.Transfers;
    [ObservableProperty] private Filter filter = Filter.All;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private string addText = "";
    [ObservableProperty] private TorrentRowViewModel? selected;
    [ObservableProperty] private bool isAdding;

    [ObservableProperty] private string downRateText = "0 B/s";
    [ObservableProperty] private string upRateText = "0 B/s";
    [ObservableProperty] private string downLimitText = "";
    [ObservableProperty] private string upLimitText = "";
    [ObservableProperty] private bool slowMode;
    [ObservableProperty] private bool slowActive;
    [ObservableProperty] private string slowText = "";
    [ObservableProperty] private int historyVersion;

    [ObservableProperty] private string dhtText = "";
    [ObservableProperty] private string portText = "";
    [ObservableProperty] private string connectionsText = "";
    [ObservableProperty] private string freeText = "";
    [ObservableProperty] private string totalsText = "";

    [ObservableProperty] private int countAll;
    [ObservableProperty] private int countActive;
    [ObservableProperty] private int countSeeding;
    [ObservableProperty] private int countPaused;
    [ObservableProperty] private int countDone;
    [ObservableProperty] private int countErrors;

    [ObservableProperty] private string? clipboardOffer;
    [ObservableProperty] private string clipboardOfferText = "";

    public bool IsTransfers => Page == Page.Transfers;
    public bool IsSpeedTest => Page == Page.SpeedTest;
    public bool IsSettings => Page == Page.Settings;
    public bool IsEmpty => CountAll == 0;
    public bool NothingMatches => CountAll > 0 && Rows.Count == 0;
    public bool HasSelection => Selected != null;

    partial void OnPageChanged(Page value)
    {
        OnPropertyChanged(nameof(IsTransfers));
        OnPropertyChanged(nameof(IsSpeedTest));
        OnPropertyChanged(nameof(IsSettings));
        if (value == Page.Settings) Settings.Reload();
        if (value == Page.SpeedTest) SpeedTest.ShowPrevious();
    }

    partial void OnFilterChanged(Filter value) => Rebuild();
    partial void OnSearchChanged(string value) => Rebuild();
    partial void OnCountAllChanged(int value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnCountErrorsChanged(int value) => OnPropertyChanged(nameof(HasErrors));
    public bool HasErrors => CountErrors > 0;

    partial void OnSelectedChanged(TorrentRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        LoadDetails(value);
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

    [RelayCommand] private void ShowTransfers() => Page = Page.Transfers;
    [RelayCommand] private void ShowSpeedTest() => Page = Page.SpeedTest;
    [RelayCommand] private void ShowSettings() => Page = Page.Settings;
    [RelayCommand] private void SetFilter(Filter f) => Filter = f;

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
        SlowActive = service.SlowActive;
        var (down, up) = SpeedSchedule.EffectiveLimits(s, DateTime.Now);
        DownLimitText = down > 0 ? "limit " + Format.Speed(down) : "";
        UpLimitText = up > 0 ? "limit " + Format.Speed(up) : "";
        SlowText = s.SlowMode ? "Slow mode on"
                 : SlowActive ? $"Slow mode until {Format.ClockMinute(s.ScheduleToMinute)}"
                 : s.ScheduleEnabled ? $"Slow from {Format.ClockMinute(s.ScheduleFromMinute)}"
                 : "Slow mode";

        DhtText = !s.Dht ? "DHT off" : service.DhtReady ? $"DHT {service.DhtNodes} nodes" : "DHT connecting";
        PortText = $"Port {s.ListenPort}" + (s.PortForwarding ? (service.PortMapped ? ", forwarded" : ", not forwarded") : "");
        ConnectionsText = $"{service.OpenConnections} connections";
        var free = AddTorrentViewModel.FreeSpace(s.DownloadDir);
        FreeText = free is { } f ? $"{Format.Size(f)} free" : "";

        var entries = service.Entries;
        TotalsText = $"{Format.Size(entries.Sum(e => e.Record.Downloaded))} down, {Format.Size(entries.Sum(e => e.Record.Uploaded))} up all time";
    }

    // ------------------------------------------------------------------ the list

    private void AddRow(TorrentEntry e)
    {
        if (rowsById.ContainsKey(e.Id)) return;
        var row = new TorrentRowViewModel(e, this);
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
        Filter.Active => r.Status is EntryStatus.Downloading or EntryStatus.Metadata or EntryStatus.Starting
                                   or EntryStatus.Queued or EntryStatus.Checking or EntryStatus.Moving,
        Filter.Seeding => r.Status == EntryStatus.Seeding,
        Filter.Paused => r.Status == EntryStatus.Paused,
        Filter.Done => r.Status is EntryStatus.Done or EntryStatus.Seeding,
        Filter.Errors => r.Status == EntryStatus.Error,
        _ => true,
    };

    /// <summary>Brings the visible list in line with the filter and search, moving as little as possible.</summary>
    private void Rebuild()
    {
        var all = rowsById.Values.OrderByDescending(r => r.Entry.Record.AddedUtc).ToList();
        CountAll = all.Count;
        CountActive = all.Count(r => Matches(r, Filter.Active));
        CountSeeding = all.Count(r => Matches(r, Filter.Seeding));
        CountPaused = all.Count(r => Matches(r, Filter.Paused));
        CountDone = all.Count(r => Matches(r, Filter.Done));
        CountErrors = all.Count(r => Matches(r, Filter.Errors));

        var term = Search.Trim();
        var wanted = all.Where(r => Matches(r, Filter) &&
                                    (term.Length == 0 || r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
                        .ToList();

        for (var i = Rows.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Rows[i])) Rows.RemoveAt(i);
        for (var i = 0; i < wanted.Count; i++)
        {
            var at = Rows.IndexOf(wanted[i]);
            if (at == i) continue;
            if (at >= 0) Rows.Move(at, i);
            else Rows.Insert(i, wanted[i]);
        }
        OnPropertyChanged(nameof(NothingMatches));
    }

    // ------------------------------------------------------------------ adding

    [RelayCommand]
    private async Task AddFromBoxAsync()
    {
        var text = AddText;
        if (string.IsNullOrWhiteSpace(text))
        {
            await AddFilesAsync();
            return;
        }
        if (await AddSourceAsync(text)) AddText = "";
    }

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        foreach (var path in await Shell.PickTorrentFilesAsync())
            await AddSourceAsync(path);
    }

    /// <summary>Anything: a magnet, a hash, a link to a .torrent, or a file. Used by paste, drop, the command line and the watch folder.</summary>
    public async Task<bool> AddSourceAsync(string text, bool quiet = false)
    {
        var (kind, value) = TorrentInput.Classify(text);
        if (kind == TorrentInputKind.None)
        {
            Shell.Notify("Not a torrent", "Paste a magnet link, an info hash, or a link to a .torrent file.", error: true);
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
                    Shell.Notify("That magnet link is broken", "It is missing or garbles the info hash.", error: true);
                    return false;
                }
                if (Existing(magnet.InfoHashes)) return true;
            }
            else
            {
                bytes = await service.FetchTorrentAsync(value);
                var torrent = Torrent.Load(bytes);
                if (Existing(torrent.InfoHashes)) return true;
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
            Page = Page.Transfers;
            if (rowsById.TryGetValue(entry.Id, out var row)) Selected = row;
            await service.TickAsync();
            return true;
        }
        catch (DuplicateTorrentException dup)
        {
            Select(dup.Existing);
            Shell.Notify("Already in the list", dup.Existing.Name);
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

    private bool Existing(InfoHashes hashes)
    {
        if (service.Find(hashes) is not { } e) return false;
        Select(e);
        Shell.Notify("Already in the list", e.Name);
        return true;
    }

    private void Select(TorrentEntry e)
    {
        Page = Page.Transfers;
        Filter = Filter.All;
        Search = "";
        if (rowsById.TryGetValue(e.Id, out var row)) Selected = row;
    }

    /// <summary>Called when the window comes to the front. Offers a magnet the user just copied.</summary>
    public void OfferClipboard(string? text)
    {
        if (!service.Settings.WatchClipboard || !TorrentInput.IsWorthOffering(text)) return;
        var (_, value) = TorrentInput.Classify(text);
        if (dismissedClipboard.Contains(value) || !MagnetLink.TryParse(value, out var magnet)) return;
        if (service.Find(magnet.InfoHashes) != null) return;
        ClipboardOffer = value;
        ClipboardOfferText = string.IsNullOrEmpty(magnet.Name) ? "There is a magnet link on your clipboard." : $"\"{magnet.Name}\" is on your clipboard.";
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

    // ------------------------------------------------------------------ row actions

    [RelayCommand]
    private async Task TogglePauseAsync(TorrentRowViewModel? row)
    {
        if (row == null) return;
        if (row.Entry.Record.WantsToRun) await service.PauseAsync(row.Entry);
        else await service.ResumeAsync(row.Entry);
        row.Refresh();
        Rebuild();
    }

    [RelayCommand]
    private async Task ForceStartAsync(TorrentRowViewModel? row)
    {
        if (row == null) return;
        await service.ResumeAsync(row.Entry, force: true);
        row.Refresh();
    }

    [RelayCommand]
    private async Task OpenFolderAsync(TorrentRowViewModel? row)
    {
        if (row == null) return;
        var e = row.Entry;
        var path = e.HasMetadata && e.Manager.Files.Count > 1 ? e.Manager.ContainingDirectory : e.Record.SaveDir;
        if (!Directory.Exists(path)) path = e.Record.SaveDir;
        await Shell.OpenAsync(path);
    }

    [RelayCommand]
    private async Task RemoveAsync(TorrentRowViewModel? row)
    {
        if (row == null) return;
        var (ok, deleteFiles) = await Shell.ConfirmRemoveAsync(row.Name, row.Entry.HasMetadata);
        if (!ok) return;
        try { await service.RemoveAsync(row.Entry, deleteFiles); }
        catch (Exception ex) { Shell.Notify("Could not remove", ex.Message, error: true); }
    }

    [RelayCommand]
    private async Task ToggleSequentialAsync(TorrentRowViewModel? row)
    {
        if (row == null) return;
        await service.SetSequentialAsync(row.Entry, !row.Entry.Record.Sequential);
        row.Refresh();
        if (row == Selected) DetailSequential = row.Sequential;
    }

    [RelayCommand]
    private async Task MoveAsync(TorrentRowViewModel? row)
    {
        if (row == null) return;
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

    [RelayCommand]
    private async Task RecheckAsync(TorrentRowViewModel? row)
    {
        if (row == null) return;
        try { await service.RecheckAsync(row.Entry); }
        catch (Exception ex) { Shell.Notify("Could not check the files", ex.Message, error: true); }
    }

    [RelayCommand]
    private async Task CopyMagnetAsync(TorrentRowViewModel? row)
    {
        if (row == null) return;
        var e = row.Entry;
        var link = e.Manager.Torrent is { } t
            ? new MagnetLink(t.InfoHashes, t.Name, t.AnnounceUrls.SelectMany(x => x).ToList(), null, t.Size).ToV1String()
            : e.Record.Magnet ?? "";
        await Shell.CopyAsync(link);
        Shell.Notify("Magnet link copied", e.Name);
    }

    [RelayCommand] private Task PauseAllAsync() => service.PauseAllAsync();
    [RelayCommand] private Task ResumeAllAsync() => service.ResumeAllAsync();

    [RelayCommand]
    private void ToggleSlowMode() => SlowMode = !SlowMode;
}
