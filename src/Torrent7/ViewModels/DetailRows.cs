using CommunityToolkit.Mvvm.ComponentModel;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Connections;
using MonoTorrent.Trackers;
using Torrent7.Core;

namespace Torrent7.ViewModels;

/// <summary>A file inside the selected torrent, with its own tick box and priority.</summary>
public sealed partial class FileRowViewModel : ObservableObject
{
    private readonly Func<FileRowViewModel, Priority, Task> setPriority;
    private bool syncing;

    public FileRowViewModel(ITorrentManagerFile file, Func<FileRowViewModel, Priority, Task> setPriority)
    {
        File = file;
        this.setPriority = setPriority;
        Path = file.Path;
        SizeText = Format.Size(file.Length);
        Refresh();
    }

    public ITorrentManagerFile File { get; }
    public string Path { get; }
    public long Length => File.Length;
    public string SizeText { get; }

    [ObservableProperty] private bool include;
    [ObservableProperty] private int priorityIndex;
    [ObservableProperty] private double progress;
    [ObservableProperty] private string progressText = "";

    /// <summary>A skipped file's meter fades back, so the wanted ones stand out.</summary>
    public double MeterOpacity => Include ? 1 : 0.35;

    partial void OnIncludeChanged(bool oldValue, bool newValue) => OnPropertyChanged(nameof(MeterOpacity));

    public static IReadOnlyList<string> PriorityNames { get; } = ["Low", "Normal", "High"];

    public void Refresh()
    {
        syncing = true;
        Include = File.Priority != Priority.DoNotDownload;
        PriorityIndex = File.Priority switch
        {
            Priority.Lowest or Priority.Low => 0,
            Priority.High or Priority.Highest or Priority.Immediate => 2,
            _ => 1,
        };
        var bits = File.BitField;
        Progress = bits.Length == 0 ? 100 : 100.0 * bits.TrueCount / bits.Length;
        ProgressText = Format.Percent(Progress);
        syncing = false;
    }

    partial void OnIncludeChanged(bool value)
    {
        if (syncing) return;
        _ = setPriority(this, value ? PriorityFromIndex(PriorityIndex) : Priority.DoNotDownload);
    }

    partial void OnPriorityIndexChanged(int value)
    {
        if (syncing || !Include) return;
        _ = setPriority(this, PriorityFromIndex(value));
    }

    private static Priority PriorityFromIndex(int i) => i switch { 0 => Priority.Low, 2 => Priority.High, _ => Priority.Normal };
}

public sealed record PeerRow(string Address, string Client, string Down, string Up, string Have, string Flags);

public sealed record TrackerRow(string Url, string State, string Seeds, string Peers, string Message);

internal static class DetailReaders
{
    public static PeerRow ToRow(PeerId p, int pieceCount)
    {
        var flags = new List<string>();
        if (p.IsSeeder) flags.Add("seed");
        if (p.ConnectionDirection == Direction.Incoming) flags.Add("incoming");
        if (p.EncryptionType != EncryptionType.PlainText) flags.Add("encrypted");
        var have = pieceCount > 0 && p.BitField.Length > 0 ? Format.Percent(100.0 * p.BitField.TrueCount / p.BitField.Length) : "";
        return new PeerRow(
            p.Uri.Host + ":" + p.Uri.Port,
            PeerClients.Name(p.PeerID.Span, p.ClientApp.Client.ToString()),
            Format.Speed(p.Monitor.DownloadRate),
            Format.Speed(p.Monitor.UploadRate),
            have,
            string.Join(", ", flags));
    }

    public static IEnumerable<TrackerRow> Trackers(TorrentManager m)
    {
        foreach (var tier in m.TrackerManager.Tiers)
        {
            foreach (var t in tier.Trackers)
            {
                ScrapeInfo? scrape = tier.ScrapeInfo.TryGetValue(m.InfoHashes.V1OrV2, out var s) ? s : null;
                var state = t.Status switch
                {
                    TrackerState.Ok => "Working",
                    TrackerState.Connecting => "Connecting",
                    TrackerState.Offline => "Not responding",
                    TrackerState.InvalidResponse => "Bad reply",
                    _ => tier.ActiveTracker == t ? "Waiting" : "Standby",
                };
                yield return new TrackerRow(
                    t.Uri.ToString(),
                    state,
                    scrape is { Complete: >= 0 } sc ? sc.Complete.ToString() : "",
                    scrape is { Incomplete: >= 0 } si ? si.Incomplete.ToString() : "",
                    !string.IsNullOrEmpty(t.FailureMessage) ? t.FailureMessage : t.WarningMessage ?? "");
            }
        }
    }
}
