using CommunityToolkit.Mvvm.ComponentModel;
using Torrent7.Core;

namespace Torrent7.ViewModels;

/// <summary>One line in the list. Refreshed once a second from the entry; setters only raise on change.</summary>
public sealed partial class TorrentRowViewModel(TorrentEntry entry, MainViewModel owner) : ObservableObject
{
    public TorrentEntry Entry { get; } = entry;

    /// <summary>The row's menu lives in a popup outside the list, so it reaches the commands through here.</summary>
    public MainViewModel Owner { get; } = owner;

    [ObservableProperty] private string name = "";
    [ObservableProperty] private EntryStatus status;
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private double progress;
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private string sizeText = "";
    [ObservableProperty] private string peersText = "";
    [ObservableProperty] private string etaText = "";
    [ObservableProperty] private string downText = "";
    [ObservableProperty] private string upText = "";
    [ObservableProperty] private bool showSpeeds;
    [ObservableProperty] private bool canPause;
    [ObservableProperty] private bool isDone;
    [ObservableProperty] private bool isError;
    [ObservableProperty] private bool isIdle;
    [ObservableProperty] private bool isMoving;
    [ObservableProperty] private bool sequential;
    [ObservableProperty] private string? errorText;
    [ObservableProperty] private int historyVersion;

    public void Refresh()
    {
        var e = Entry;
        Name = e.Name;
        Status = e.Status;
        Sequential = e.Record.Sequential;
        ErrorText = e.ErrorText;
        IsError = Status == EntryStatus.Error;
        IsDone = Status is EntryStatus.Done or EntryStatus.Seeding;
        IsIdle = Status is EntryStatus.Paused or EntryStatus.Queued;
        IsMoving = Status == EntryStatus.Moving;
        CanPause = e.Record.WantsToRun;
        Progress = e.HasMetadata ? e.Progress : 0;

        StatusText = Status switch
        {
            EntryStatus.Queued => "Queued",
            EntryStatus.Starting => "Starting",
            EntryStatus.Metadata => "Fetching file list",
            EntryStatus.Downloading => e.Record.Forced ? "Downloading, forced" : "Downloading",
            EntryStatus.Seeding => "Seeding",
            EntryStatus.Done => "Done",
            EntryStatus.Paused => "Paused",
            EntryStatus.Checking => "Checking files",
            EntryStatus.Moving => "Moving files",
            EntryStatus.Error => "Stopped with an error",
            _ => "",
        };

        if (e.HasMetadata)
        {
            ProgressText = Format.Percent(e.Progress);
            var wanted = e.WantedSize;
            SizeText = e.IsComplete
                ? Format.Size(wanted) + (wanted < e.Size ? $" of {Format.Size(e.Size)} selected" : "")
                : $"{Format.Size(e.WantedDone)} of {Format.Size(wanted)}";
        }
        else
        {
            ProgressText = "";
            SizeText = "Size unknown until the file list arrives";
        }

        var running = e.IsRunning;
        ShowSpeeds = running;
        DownText = Format.Speed(e.DownRate);
        UpText = Format.Speed(e.UpRate);
        PeersText = running ? $"{Plural(e.Seeds, "seed")}, {Plural(e.Leechers, "peer")} connected" : "";
        EtaText = Status == EntryStatus.Downloading
            ? (e.Eta is { } eta ? Format.Eta(eta) + " left" : "waiting for peers")
            : Status == EntryStatus.Seeding ? $"ratio {Format.Ratio(e.Ratio)}" : "";

        HistoryVersion++;
    }

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    /// <summary>What a screen reader announces for the row.</summary>
    public override string ToString() => $"{Name}, {StatusText}, {ProgressText}";
}
