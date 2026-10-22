using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoTorrent;
using Torrent7.Core;

namespace Torrent7.ViewModels;

public sealed partial class FileChoice(string path, long length, Action changed) : ObservableObject
{
    public string Path { get; } = path;
    public long Length { get; } = length;
    public string SizeText { get; } = Format.Size(length);

    [ObservableProperty] private bool include = true;

    partial void OnIncludeChanged(bool value) => changed();
}

/// <summary>
/// The add dialog: where it goes, which files, start now or later. A magnet has no file list
/// yet, so the dialog asks the swarm for one and can be told not to wait.
/// </summary>
public sealed partial class AddTorrentViewModel : ObservableObject
{
    private readonly TorrentService service;
    private readonly IShell shell;
    private readonly CancellationTokenSource fetch = new();
    private byte[]? torrentBytes;

    public AddTorrentViewModel(TorrentService service, IShell shell, byte[]? torrentBytes, MagnetLink? magnet)
    {
        this.service = service;
        this.shell = shell;
        this.torrentBytes = torrentBytes;
        Magnet = magnet;
        SaveDir = service.Settings.DownloadDir;
        Start = service.Settings.StartImmediately;

        if (torrentBytes != null) LoadFiles(torrentBytes);
        else if (magnet != null)
        {
            Name = string.IsNullOrEmpty(magnet.Name) ? "Magnet link" : magnet.Name;
            IsFetching = true;
            _ = FetchAsync(magnet);
        }
        UpdateSummary();
    }

    public MagnetLink? Magnet { get; }
    public ObservableCollection<FileChoice> Files { get; } = [];

    [ObservableProperty] private string name = "";
    [ObservableProperty] private string saveDir = "";
    [ObservableProperty] private bool start = true;
    [ObservableProperty] private bool sequential;
    [ObservableProperty] private bool isFetching;
    [ObservableProperty] private string? fetchError;
    [ObservableProperty] private string summary = "";
    [ObservableProperty] private string spaceText = "";
    [ObservableProperty] private bool spaceShort;
    [ObservableProperty] private bool isPrivate;
    [ObservableProperty] private bool hasFiles;
    [ObservableProperty] private bool noFileList = true;

    /// <summary>Set by the window when the user confirms; read back by the caller.</summary>
    public AddRequest? Result { get; private set; }
    public event Action? CloseRequested;

    public bool CanAdd => !IsFetching && (Files.Count == 0 || Files.Any(f => f.Include)) && !string.IsNullOrWhiteSpace(SaveDir);

    partial void OnIsFetchingChanged(bool value) => OnPropertyChanged(nameof(CanAdd));
    partial void OnSaveDirChanged(string value)
    {
        OnPropertyChanged(nameof(CanAdd));
        UpdateSummary();
    }

    private async Task FetchAsync(MagnetLink magnet)
    {
        try
        {
            var bytes = await service.FetchMetadataAsync(magnet, fetch.Token);
            torrentBytes = bytes;
            LoadFiles(bytes);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { FetchError = "Could not get the file list: " + ex.Message; }
        finally
        {
            IsFetching = false;
            UpdateSummary();
        }
    }

    private void LoadFiles(byte[] bytes)
    {
        var torrent = Torrent.Load(bytes);
        Name = torrent.Name;
        IsPrivate = torrent.IsPrivate;
        Files.Clear();
        foreach (var f in torrent.Files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
            Files.Add(new FileChoice(f.Path, f.Length, UpdateSummary));
        HasFiles = Files.Count > 1;
        NoFileList = Files.Count == 0;
    }

    private void UpdateSummary()
    {
        if (Files.Count == 0)
        {
            Summary = IsFetching ? "Asking peers for the file list" : "Files appear once the download starts";
            SpaceText = "";
            SpaceShort = false;
            OnPropertyChanged(nameof(CanAdd));
            return;
        }

        var chosen = Files.Where(f => f.Include).ToList();
        var bytes = chosen.Sum(f => f.Length);
        Summary = chosen.Count == Files.Count
            ? $"{Files.Count} {(Files.Count == 1 ? "file" : "files")}, {Format.Size(bytes)}"
            : $"{chosen.Count} of {Files.Count} files, {Format.Size(bytes)}";

        var free = FreeSpace(SaveDir);
        if (free is { } f)
        {
            SpaceShort = f < bytes;
            SpaceText = SpaceShort
                ? $"Not enough space: {Format.Size(bytes)} needed, {Format.Size(f)} free"
                : $"{Format.Size(f)} free on this drive";
        }
        else
        {
            SpaceText = "";
            SpaceShort = false;
        }
        OnPropertyChanged(nameof(CanAdd));
    }

    internal static long? FreeSpace(string dir)
    {
        try
        {
            var probe = dir;
            while (!string.IsNullOrEmpty(probe) && !Directory.Exists(probe))
                probe = Path.GetDirectoryName(probe);
            if (string.IsNullOrEmpty(probe)) return null;
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(probe))!).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var picked = await shell.PickFolderAsync(SaveDir);
        if (picked != null) SaveDir = picked;
    }

    [RelayCommand]
    private void SelectAll(bool on)
    {
        foreach (var f in Files) f.Include = on;
    }

    [RelayCommand]
    private void Add()
    {
        if (!CanAdd) return;
        var skip = Files.Where(f => !f.Include).Select(f => f.Path).ToHashSet();
        Result = new AddRequest(torrentBytes, Magnet, SaveDir.Trim(), Start, Sequential, skip.Count > 0 ? skip : null);
        fetch.Cancel();
        CloseRequested?.Invoke();
    }

    /// <summary>Adds the magnet as it is; the files show up once peers send the list.</summary>
    [RelayCommand]
    private void AddWithoutWaiting()
    {
        fetch.Cancel();
        Result = new AddRequest(null, Magnet, SaveDir.Trim(), Start, Sequential);
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        fetch.Cancel();
        Result = null;
        CloseRequested?.Invoke();
    }

    public void Closed() => fetch.Cancel();
}
