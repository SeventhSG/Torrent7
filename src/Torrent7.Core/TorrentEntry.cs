using MonoTorrent;
using MonoTorrent.Client;

namespace Torrent7.Core;

public enum EntryStatus { Queued, Starting, Metadata, Downloading, Seeding, Done, Paused, Checking, Moving, Error }

/// <summary>One torrent: the engine's manager plus what we remember about it.</summary>
public sealed class TorrentEntry
{
    internal TorrentEntry(TorrentRecord record, TorrentManager manager)
    {
        Record = record;
        Manager = manager;
    }

    public TorrentRecord Record { get; }
    public TorrentManager Manager { get; internal set; }

    public string Id => Record.Id;
    public string Name => Manager.Torrent?.Name ?? (string.IsNullOrEmpty(Record.Name) ? Manager.Name ?? Id : Record.Name);
    public bool HasMetadata => Manager.HasMetadata && Manager.Torrent != null;
    public bool IsPrivate => Manager.Torrent?.IsPrivate == true;

    public long Size => Manager.Torrent?.Size ?? 0;

    /// <summary>Bytes of the files the user actually wants, which is what "done" means.</summary>
    public long WantedSize => HasMetadata
        ? Manager.Files.Where(f => f.Priority != Priority.DoNotDownload).Sum(f => f.Length)
        : 0;

    /// <summary>The engine's partial progress already counts only wanted pieces.</summary>
    public long WantedDone => HasMetadata ? (long)(WantedSize * Math.Clamp(Manager.PartialProgress, 0, 100) / 100) : 0;

    /// <summary>0 to 100, counting only wanted files.</summary>
    public double Progress => HasMetadata ? Manager.PartialProgress : 0;

    public bool IsComplete => HasMetadata && (Manager.Complete || Manager.PartialProgress >= 100);

    public long DownRate => Manager.Monitor.DownloadRate;
    public long UpRate => Manager.Monitor.UploadRate;

    /// <summary>
    /// Uploaded over downloaded. A torrent seeded from files that were already on disk
    /// downloaded nothing, so its size stands in, the way other clients count it.
    /// </summary>
    public double Ratio
    {
        get
        {
            var basis = Record.Downloaded > 0 ? Record.Downloaded : WantedSize;
            return basis > 0 ? Record.Uploaded / (double)basis : 0;
        }
    }

    public TimeSpan? Eta => IsComplete ? TimeSpan.Zero : Format.EtaFor(Math.Max(0, WantedSize - WantedDone), DownRate);

    public int Seeds => Manager.Peers.Seeds;
    public int Leechers => Manager.Peers.Leechs;
    public int PeersAvailable => Manager.Peers.Available;
    public int Connections => Manager.OpenConnections;

    public string? ErrorText { get; internal set; }

    public EntryStatus Status
    {
        get
        {
            if (Moving) return EntryStatus.Moving;
            if (ErrorText != null || Manager.State == TorrentState.Error) return EntryStatus.Error;
            // Paused means paused from the moment it is asked, not once the last peer has hung up.
            if (!Record.WantsToRun && Manager.State is not (TorrentState.Hashing or TorrentState.HashingPaused or TorrentState.FetchingHashes))
                return IsComplete ? EntryStatus.Done : EntryStatus.Paused;
            switch (Manager.State)
            {
                case TorrentState.Hashing:
                case TorrentState.HashingPaused:
                case TorrentState.FetchingHashes:
                    return EntryStatus.Checking;
                case TorrentState.Metadata:
                    return EntryStatus.Metadata;
                case TorrentState.Starting:
                    return EntryStatus.Starting;
                case TorrentState.Downloading:
                    return EntryStatus.Downloading;
                case TorrentState.Seeding:
                    return EntryStatus.Seeding;
            }
            if (!Record.WantsToRun) return IsComplete ? EntryStatus.Done : EntryStatus.Paused;
            return IsComplete ? EntryStatus.Done : EntryStatus.Queued;
        }
    }

    public bool IsRunning => Manager.State is TorrentState.Downloading or TorrentState.Seeding or TorrentState.Starting
        or TorrentState.Metadata or TorrentState.Hashing or TorrentState.FetchingHashes;

    internal bool Moving { get; set; }

    /// <summary>Set while "download in order" is on.</summary>
    internal MonoTorrent.PiecePicking.StreamingPieceRequester? Sequencer { get; set; }

    // Session counters reset when a manager restarts, so the deltas are tracked here.
    internal long LastSent;
    internal long LastReceived;

    /// <summary>The last minute of download speed, one sample a second, for the row sparkline.</summary>
    public SpeedHistory History { get; } = new(60);
}

/// <summary>A fixed ring of speed samples.</summary>
public sealed class SpeedHistory(int capacity)
{
    private readonly long[] down = new long[capacity];
    private readonly long[] up = new long[capacity];
    private int head;

    public int Capacity => capacity;
    public int Count { get; private set; }

    public void Add(long downBps, long upBps)
    {
        down[head] = downBps;
        up[head] = upBps;
        head = (head + 1) % capacity;
        Count = Math.Min(Count + 1, capacity);
    }

    /// <summary>Oldest first.</summary>
    public IEnumerable<(long Down, long Up)> Samples()
    {
        var start = (head - Count + capacity) % capacity;
        for (var i = 0; i < Count; i++)
        {
            var k = (start + i) % capacity;
            yield return (down[k], up[k]);
        }
    }

    public long Peak => Count == 0 ? 0 : Samples().Max(s => Math.Max(s.Down, s.Up));
}


