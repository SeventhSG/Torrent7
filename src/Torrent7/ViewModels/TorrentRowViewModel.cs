using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Torrent7.Core;

namespace Torrent7.ViewModels;

/// <summary>
/// One line of the torrent table. Refreshed once a second from the entry; setters only raise
/// on change, so a quiet torrent costs nothing to redraw. Every column has a raw value
/// next to its text, because the table sorts on the raw value.
/// </summary>
public sealed partial class TorrentRowViewModel(TorrentEntry entry) : ObservableObject
{
    public TorrentEntry Entry { get; } = entry;

    [ObservableProperty] private string name = "";
    [ObservableProperty] private EntryStatus status;
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private Geometry? stateIcon;
    [ObservableProperty] private IBrush? stateBrush;
    [ObservableProperty] private IBrush? meterBrush;
    [ObservableProperty] private double progress;
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private long sizeBytes;
    [ObservableProperty] private string sizeText = "";
    [ObservableProperty] private long downRate;
    [ObservableProperty] private string downText = "";
    [ObservableProperty] private long upRate;
    [ObservableProperty] private string upText = "";
    [ObservableProperty] private double etaSeconds = double.MaxValue;
    [ObservableProperty] private string etaText = "";
    [ObservableProperty] private int peers;
    [ObservableProperty] private string peersText = "";
    [ObservableProperty] private double ratio;
    [ObservableProperty] private string ratioText = "";
    [ObservableProperty] private DateTime added;
    [ObservableProperty] private string addedText = "";
    [ObservableProperty] private bool canPause;
    [ObservableProperty] private bool isDone;
    [ObservableProperty] private bool isError;
    [ObservableProperty] private bool isIdle;
    [ObservableProperty] private bool sequential;
    [ObservableProperty] private string? errorText;

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
        CanPause = e.Record.WantsToRun;

        StatusText = Status switch
        {
            EntryStatus.Queued => "Queued",
            EntryStatus.Starting => "Starting",
            EntryStatus.Metadata => "Getting file list",
            EntryStatus.Downloading => e.Record.Forced ? "Downloading (forced)" : e.Record.Sequential ? "Downloading in order" : "Downloading",
            EntryStatus.Seeding => "Seeding",
            EntryStatus.Done => "Completed",
            EntryStatus.Paused => "Paused",
            EntryStatus.Checking => "Checking",
            EntryStatus.Moving => "Moving",
            EntryStatus.Error => "Error",
            _ => "",
        };
        (StateIcon, StateBrush) = Look(Status);
        // The meter is amber while data comes in, quiet once it is done, red when it failed.
        MeterBrush = Res.Get<IBrush>(IsError ? "DangerBrush" : IsDone ? "UpBrush" : IsIdle ? "TextFaintBrush" : "AccentBrush");

        Progress = e.HasMetadata ? e.Progress : 0;
        ProgressText = e.HasMetadata ? Format.Percent(e.Progress) : "";
        SizeBytes = e.WantedSize;
        SizeText = e.HasMetadata ? Format.Size(e.WantedSize) : "";

        var running = e.IsRunning;
        DownRate = running ? e.DownRate : 0;
        UpRate = running ? e.UpRate : 0;
        // A blank is quieter than a column of "0 B/s".
        DownText = DownRate > 0 ? Format.Speed(DownRate) : "";
        UpText = UpRate > 0 ? Format.Speed(UpRate) : "";

        var eta = Status == EntryStatus.Downloading ? e.Eta : null;
        EtaSeconds = eta?.TotalSeconds ?? double.MaxValue;
        EtaText = eta is { } t ? Format.Eta(t) : (Status == EntryStatus.Downloading ? "unknown" : "");

        Peers = running ? e.Seeds + e.Leechers : 0;
        PeersText = running ? $"{e.Seeds} / {e.Leechers}" : "";
        Ratio = e.Ratio;
        RatioText = Format.Ratio(Ratio);
        Added = e.Record.AddedUtc;
        AddedText = e.Record.AddedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    /// <summary>The state icon and its colour: amber only when data is moving in.</summary>
    private static (Geometry?, IBrush?) Look(EntryStatus s)
    {
        string icon, brush;
        switch (s)
        {
            case EntryStatus.Downloading: case EntryStatus.Starting: case EntryStatus.Metadata:
                icon = "IconCircleDown"; brush = "AccentBrush"; break;
            case EntryStatus.Seeding:
                icon = "IconCircleUp"; brush = "UpBrush"; break;
            case EntryStatus.Done:
                icon = "IconCircleCheck"; brush = "TextFaintBrush"; break;
            case EntryStatus.Paused:
                icon = "IconCirclePause"; brush = "TextFaintBrush"; break;
            case EntryStatus.Queued:
                icon = "IconClock"; brush = "TextFaintBrush"; break;
            case EntryStatus.Checking: case EntryStatus.Moving:
                icon = "IconRefresh"; brush = "TextDimBrush"; break;
            default:
                icon = "IconAlert"; brush = "DangerBrush"; break;
        }

        return (Res.Get<Geometry>(icon), Res.Get<IBrush>(brush));
    }

    /// <summary>What a screen reader announces for the row.</summary>
    public override string ToString() => $"{Name}, {StatusText}, {ProgressText}";
}
