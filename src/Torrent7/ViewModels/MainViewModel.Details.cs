using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoTorrent;
using Torrent7.Core;

namespace Torrent7.ViewModels;

/// <summary>The panel under the list: files, peers, trackers and the facts of the selected torrent.</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<FileRowViewModel> Files { get; } = [];
    public ObservableCollection<PeerRow> Peers { get; } = [];
    public ObservableCollection<TrackerRow> Trackers { get; } = [];

    [ObservableProperty] private int detailTab;
    [ObservableProperty] private bool detailSequential;
    [ObservableProperty] private decimal? detailDownLimit;
    [ObservableProperty] private decimal? detailUpLimit;
    [ObservableProperty] private string infoSaveDir = "";
    [ObservableProperty] private string infoHash = "";
    [ObservableProperty] private string infoSize = "";
    [ObservableProperty] private string infoPieces = "";
    [ObservableProperty] private string infoAdded = "";
    [ObservableProperty] private string infoCompleted = "";
    [ObservableProperty] private string infoTransferred = "";
    [ObservableProperty] private string infoComment = "";
    [ObservableProperty] private string infoPrivate = "";
    [ObservableProperty] private string? infoError;
    [ObservableProperty] private string filesEmptyText = "";
    private bool filesLoaded;
    private bool loadingDetails;

    partial void OnDetailTabChanged(int value) => RefreshDetails();

    private void LoadDetails(TorrentRowViewModel? row)
    {
        loadingDetails = true;
        Files.Clear();
        Peers.Clear();
        Trackers.Clear();
        filesLoaded = false;
        if (row != null)
        {
            var r = row.Entry.Record;
            DetailSequential = r.Sequential;
            DetailDownLimit = r.DownLimitKiB;
            DetailUpLimit = r.UpLimitKiB;
            InfoHash = row.Entry.Id;
        }
        loadingDetails = false;
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        if (Selected is not { } row) return;
        var e = row.Entry;

        if (!filesLoaded && e.HasMetadata)
        {
            foreach (var f in e.Manager.Files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
                Files.Add(new FileRowViewModel(f, (fr, p) => SetFilePriorityAsync(e, fr, p)));
            filesLoaded = true;
        }
        FilesEmptyText = e.HasMetadata ? "" : "The file list arrives from peers once the download starts.";

        switch (DetailTab)
        {
            case 0:
                foreach (var f in Files) f.Refresh();
                break;
            case 1:
                _ = RefreshPeersAsync(e);
                break;
            case 2:
                Sync(Trackers, DetailReaders.Trackers(e.Manager).ToList());
                break;
        }

        var t = e.Manager.Torrent;
        InfoSaveDir = e.HasMetadata && e.Manager.Files.Count > 1 ? e.Manager.ContainingDirectory : e.Record.SaveDir;
        InfoSize = t != null ? $"{Format.Size(t.Size)} in {e.Manager.Files.Count} {(e.Manager.Files.Count == 1 ? "file" : "files")}" : "Not known yet";
        InfoPieces = t != null ? $"{t.PieceCount} pieces of {Format.Size(t.PieceLength)}" : "";
        InfoAdded = e.Record.AddedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        InfoCompleted = e.Record.CompletedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "Not yet";
        InfoTransferred = $"{Format.Size(e.Record.Downloaded)} down, {Format.Size(e.Record.Uploaded)} up, ratio {Format.Ratio(e.Ratio)}";
        InfoComment = t?.Comment ?? "";
        InfoPrivate = t == null ? "" : t.IsPrivate ? "Private: DHT, peer exchange and tracker boost are off for it" : "Public";
        InfoError = e.ErrorText;
    }

    private async Task RefreshPeersAsync(TorrentEntry e)
    {
        var peers = await e.Manager.GetPeersAsync();
        var pieces = e.Manager.Torrent?.PieceCount ?? 0;
        var rows = peers.OrderByDescending(p => p.Monitor.DownloadRate + p.Monitor.UploadRate)
                        .Select(p => DetailReaders.ToRow(p, pieces))
                        .ToList();
        if (Selected?.Entry == e) Sync(Peers, rows);
    }

    private static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        // Replace in place so the list does not jump to the top every second.
        for (var i = 0; i < source.Count; i++)
        {
            if (i < target.Count) { if (!Equals(target[i], source[i])) target[i] = source[i]; }
            else target.Add(source[i]);
        }
        while (target.Count > source.Count) target.RemoveAt(target.Count - 1);
    }

    private async Task SetFilePriorityAsync(TorrentEntry e, FileRowViewModel row, Priority p)
    {
        try { await service.SetFilePriorityAsync(e, row.File, p); }
        catch (Exception ex) { Shell.Notify("Could not change the file", ex.Message, error: true); }
        row.Refresh();
    }

    partial void OnDetailSequentialChanged(bool value)
    {
        if (loadingDetails || Selected is not { } row || row.Entry.Record.Sequential == value) return;
        _ = service.SetSequentialAsync(row.Entry, value);
    }

    [RelayCommand]
    private async Task ApplyDetailLimitsAsync()
    {
        if (Selected is not { } row) return;
        await service.SetLimitsAsync(row.Entry, (int)(DetailDownLimit ?? 0), (int)(DetailUpLimit ?? 0));
        Shell.Notify("Limits set", $"{row.Name}: down {Format.Limit(row.Entry.Record.DownLimitKiB)}, up {Format.Limit(row.Entry.Record.UpLimitKiB)}");
    }

    [RelayCommand]
    private async Task OpenFileAsync(FileRowViewModel? file)
    {
        if (file == null) return;
        if (!System.IO.File.Exists(file.File.FullPath))
        {
            Shell.Notify("Not on disk yet", "That file has not started downloading.", error: true);
            return;
        }
        await Shell.OpenAsync(file.File.FullPath);
    }

    [RelayCommand]
    private void SelectAllFiles(bool on)
    {
        foreach (var f in Files) f.Include = on;
    }

    [RelayCommand]
    private Task OpenSaveDirAsync() => Shell.OpenAsync(InfoSaveDir);

    [RelayCommand]
    private async Task CopyHashAsync()
    {
        await Shell.CopyAsync(InfoHash);
        Shell.Notify("Info hash copied", InfoHash);
    }
}
