using System.Net;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Connections;
using MonoTorrent.PiecePicking;

namespace Torrent7.Core;

/// <summary>What the add dialog hands over: metadata or a magnet, and where it goes.</summary>
public sealed record AddRequest(
    byte[]? TorrentBytes,
    MagnetLink? Magnet,
    string SaveDir,
    bool Start = true,
    bool Sequential = false,
    IReadOnlySet<string>? SkipFiles = null);

public sealed class DuplicateTorrentException(TorrentEntry existing)
    : Exception($"\"{existing.Name}\" is already in the list.")
{
    public TorrentEntry Existing { get; } = existing;
}

/// <summary>
/// Owns the engine and every torrent in it. The UI drives it through these methods and reads
/// state from the entries; <see cref="TickAsync"/> runs once a second and does the
/// housekeeping: limits, the queue, ratios, and saving.
/// </summary>
public sealed class TorrentService : IAsyncDisposable
{
    private readonly List<TorrentEntry> entries = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HttpClient http;
    private ClientEngine engine = null!;
    private int ticks;
    private (int Down, int Up) appliedLimits = (-1, -1);
    private bool limitsSuspended;

    public TorrentService(AppSettings settings, HttpClient http)
    {
        Settings = settings;
        this.http = http;
    }

    public AppSettings Settings { get; private set; }

    public IReadOnlyList<TorrentEntry> Entries
    {
        get { lock (entries) return entries.ToList(); }
    }

    public long DownRate => engine?.TotalDownloadRate ?? 0;
    public long UpRate => engine?.TotalUploadRate ?? 0;
    public int DhtNodes => engine?.Dht?.NodeCount ?? 0;
    public bool DhtReady => engine?.Dht?.State == MonoTorrent.Dht.DhtState.Ready;
    public int OpenConnections => engine?.ConnectionManager.OpenConnections ?? 0;
    public bool PortMapped => engine?.PortMappings.Created.Count > 0;
    public bool SlowActive { get; private set; }
    public SpeedHistory History { get; } = new(120);

    public event Action<TorrentEntry>? Added;
    public event Action<TorrentEntry>? Removed;
    public event Action<TorrentEntry>? Completed;
    public event Action<string>? Problem;

    // ------------------------------------------------------------------ lifetime

    public async Task StartAsync()
    {
        Directory.CreateDirectory(AppPaths.TorrentsDir);
        Directory.CreateDirectory(AppPaths.CacheDir);
        engine = new ClientEngine(BuildEngineSettings(Settings, DateTime.Now));
        engine.CriticalException += (_, e) =>
        {
            Log.Error("engine", e.Exception);
            Problem?.Invoke("The engine hit an error: " + e.Exception.Message);
        };

        foreach (var record in SessionStore.Load())
        {
            try { await RestoreAsync(record); }
            catch (Exception ex)
            {
                Log.Error($"could not restore {record.Name}", ex);
                Problem?.Invoke($"Could not restore \"{record.Name}\": {ex.Message}");
            }
        }
        Log.Info($"started, {entries.Count} torrents, port {Settings.ListenPort}");
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try
        {
            SaveSession();
            // Stopping writes the fast resume data, which is what makes the next start instant.
            await engine.StopAllAsync(TimeSpan.FromSeconds(8));
            engine.Dispose();
        }
        catch (Exception ex) { Log.Error("shutdown", ex); }
        finally { gate.Release(); }
    }

    private async Task RestoreAsync(TorrentRecord record)
    {
        TorrentManager manager;
        var settings = BuildTorrentSettings(record);
        // A portable folder carried to another machine keeps its torrents under a new path.
        var local = Path.Combine(AppPaths.TorrentsDir, record.Id + ".torrent");
        if ((record.TorrentFile == null || !File.Exists(record.TorrentFile)) && File.Exists(local))
            record.TorrentFile = local;
        if (record.TorrentFile != null && File.Exists(record.TorrentFile))
            manager = await engine.AddAsync(await Torrent.LoadAsync(record.TorrentFile), record.SaveDir, settings);
        else if (record.Magnet != null && MagnetLink.TryParse(record.Magnet, out var magnet))
            manager = await engine.AddAsync(magnet, record.SaveDir, settings);
        else
            throw new FileNotFoundException("its .torrent file is gone and there is no magnet link to fall back on");

        var entry = new TorrentEntry(record, manager);
        await PrepareAsync(entry);
        lock (entries) entries.Add(entry);
        Added?.Invoke(entry);
    }

    // ------------------------------------------------------------------ adding

    /// <summary>Reads a .torrent from disk or the web. Throws with a readable message on anything wrong.</summary>
    public async Task<byte[]> FetchTorrentAsync(string source, CancellationToken token = default)
    {
        byte[] bytes;
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            using var resp = await http.GetAsync(uri, token);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidDataException($"The site answered {(int)resp.StatusCode} {resp.ReasonPhrase}.");
            bytes = await resp.Content.ReadAsByteArrayAsync(token);
        }
        else
        {
            bytes = await File.ReadAllBytesAsync(source, token);
        }

        if (!Torrent.TryLoad(bytes, out _))
            throw new InvalidDataException("That is not a valid .torrent file.");
        return bytes;
    }

    /// <summary>Asks the swarm for a magnet's file list, so the add dialog can show it before anything downloads.</summary>
    public async Task<byte[]> FetchMetadataAsync(MagnetLink magnet, CancellationToken token)
    {
        var data = await engine.DownloadMetadataAsync(magnet, token);
        return data.ToArray();
    }

    public TorrentEntry? Find(InfoHashes hashes)
    {
        var id = IdOf(hashes);
        lock (entries) return entries.FirstOrDefault(e => e.Id == id);
    }

    public async Task<TorrentEntry> AddAsync(AddRequest req)
    {
        await gate.WaitAsync();
        try
        {
            var record = new TorrentRecord
            {
                SaveDir = req.SaveDir,
                WantsToRun = req.Start,
                Sequential = req.Sequential,
                AddedUtc = DateTime.UtcNow,
            };

            TorrentManager manager;
            if (req.TorrentBytes != null)
            {
                var torrent = await Torrent.LoadAsync(req.TorrentBytes);
                record.Id = IdOf(torrent.InfoHashes);
                record.Name = torrent.Name;
                if (Find(torrent.InfoHashes) is { } dup) throw new DuplicateTorrentException(dup);

                record.TorrentFile = Path.Combine(AppPaths.TorrentsDir, record.Id + ".torrent");
                await File.WriteAllBytesAsync(record.TorrentFile, req.TorrentBytes);
                if (req.Magnet != null) record.Magnet = req.Magnet.ToV1String();

                foreach (var skip in req.SkipFiles ?? new HashSet<string>())
                    record.FilePriorities[skip] = Priority.DoNotDownload;

                manager = await engine.AddAsync(torrent, req.SaveDir, BuildTorrentSettings(record));
            }
            else if (req.Magnet != null)
            {
                record.Id = IdOf(req.Magnet.InfoHashes);
                record.Name = req.Magnet.Name ?? "";
                record.Magnet = req.Magnet.ToV1String();
                if (Find(req.Magnet.InfoHashes) is { } dup) throw new DuplicateTorrentException(dup);
                manager = await engine.AddAsync(req.Magnet, req.SaveDir, BuildTorrentSettings(record));
            }
            else
            {
                throw new ArgumentException("Nothing to add.");
            }

            Directory.CreateDirectory(req.SaveDir);
            var entry = new TorrentEntry(record, manager);
            await PrepareAsync(entry);
            lock (entries) entries.Add(entry);
            SaveSession();
            Log.Info($"added {entry.Name} to {req.SaveDir}");
            Added?.Invoke(entry);
            return entry;
        }
        finally { gate.Release(); }
    }

    /// <summary>Everything a torrent needs before it first starts, whether new or restored.</summary>
    private async Task PrepareAsync(TorrentEntry entry)
    {
        var m = entry.Manager;
        m.TorrentStateChanged += (_, e) =>
        {
            if (e.NewState == TorrentState.Error)
            {
                entry.ErrorText = m.Error?.Exception?.Message ?? "Stopped with an error";
                Log.Error($"{entry.Name}: {entry.ErrorText}");
                Problem?.Invoke($"\"{entry.Name}\" stopped: {entry.ErrorText}");
            }
        };

        await ApplyFilePrioritiesAsync(entry);
        if (entry.Record.Sequential) await ApplyPickerAsync(entry);
        await BoostAsync(entry);
    }

    private async Task ApplyFilePrioritiesAsync(TorrentEntry entry)
    {
        if (!entry.HasMetadata || entry.Record.FilePriorities.Count == 0) return;
        foreach (var file in entry.Manager.Files)
            if (entry.Record.FilePriorities.TryGetValue(file.Path, out var p) && file.Priority != p)
                await entry.Manager.SetFilePriorityAsync(file, p);
    }

    private async Task BoostAsync(TorrentEntry entry)
    {
        if (!Settings.TrackerBoost) return;
        // A magnet's private flag is unknown until its metadata arrives, so it is boosted then.
        if (!entry.HasMetadata || entry.IsPrivate) return;
        var tm = entry.Manager.TrackerManager;
        if (tm.Private) return;

        var existing = tm.Tiers.SelectMany(t => t.Trackers).Select(t => t.Uri.ToString());
        foreach (var url in TrackerBoost.Missing(existing, TrackerBoost.Current))
        {
            try { await tm.AddTrackerAsync(new Uri(url)); }
            catch (Exception ex) { Log.Warn($"tracker {url}: {ex.Message}"); }
        }
    }

    // ------------------------------------------------------------------ actions

    public async Task PauseAsync(TorrentEntry entry)
    {
        entry.Record.WantsToRun = false;
        entry.Record.Forced = false;
        await StopQuietlyAsync(entry);
        SaveSession();
    }

    public async Task ResumeAsync(TorrentEntry entry, bool force = false)
    {
        entry.ErrorText = null;
        entry.Record.WantsToRun = true;
        // Resuming a finished torrent past its seed ratio is a clear request to keep seeding.
        entry.Record.Forced = force || (entry.IsComplete && Settings.SeedRatioLimit > 0 && entry.Ratio >= Settings.SeedRatioLimit);
        SaveSession();
        await TickAsync();
    }

    public async Task RemoveAsync(TorrentEntry entry, bool deleteFiles)
    {
        await gate.WaitAsync();
        try
        {
            lock (entries) entries.Remove(entry);
            await StopQuietlyAsync(entry);
            await engine.RemoveAsync(entry.Manager, deleteFiles ? RemoveMode.CacheDataAndDownloadedData : RemoveMode.CacheDataOnly);
            if (entry.Record.TorrentFile != null)
                try { File.Delete(entry.Record.TorrentFile); } catch (IOException) { }
            SaveSession();
            Log.Info($"removed {entry.Name}{(deleteFiles ? " and its files" : "")}");
        }
        finally { gate.Release(); }
        Removed?.Invoke(entry);
    }

    public async Task SetSequentialAsync(TorrentEntry entry, bool on)
    {
        entry.Record.Sequential = on;
        await ApplyPickerAsync(entry);
        SaveSession();
    }

    private async Task ApplyPickerAsync(TorrentEntry entry)
    {
        if (entry.Record.Sequential)
        {
            entry.Sequencer = new StreamingPieceRequester();
            await entry.Manager.ChangePickerAsync(entry.Sequencer);
        }
        else
        {
            entry.Sequencer = null;
            await entry.Manager.ChangePickerAsync(Factories.Default.CreatePieceRequester());
        }
    }

    public async Task SetLimitsAsync(TorrentEntry entry, int downKiB, int upKiB)
    {
        entry.Record.DownLimitKiB = Math.Max(0, downKiB);
        entry.Record.UpLimitKiB = Math.Max(0, upKiB);
        await entry.Manager.UpdateSettingsAsync(BuildTorrentSettings(entry.Record));
        SaveSession();
    }

    public async Task SetFilePriorityAsync(TorrentEntry entry, ITorrentManagerFile file, Priority priority)
    {
        await entry.Manager.SetFilePriorityAsync(file, priority);
        if (priority == Priority.Normal) entry.Record.FilePriorities.Remove(file.Path);
        else entry.Record.FilePriorities[file.Path] = priority;
        SaveSession();
    }

    /// <summary>Moves the downloaded files and keeps going from the new place.</summary>
    public async Task MoveAsync(TorrentEntry entry, string newDir)
    {
        entry.Moving = true;
        try
        {
            await StopQuietlyAsync(entry);
            await entry.Manager.MoveFilesAsync(newDir, overWriteExisting: false);
            entry.Record.SaveDir = newDir;
            SaveSession();
            Log.Info($"moved {entry.Name} to {newDir}");
        }
        finally { entry.Moving = false; }
        await TickAsync();
    }

    /// <summary>Hashes every piece again. The cure for files edited or damaged on disk.</summary>
    public async Task RecheckAsync(TorrentEntry entry)
    {
        await StopQuietlyAsync(entry);
        entry.ErrorText = null;
        await entry.Manager.HashCheckAsync(autoStart: false);
        await TickAsync();
    }

    public async Task PauseAllAsync()
    {
        foreach (var e in Entries) await PauseAsync(e);
    }

    public async Task ResumeAllAsync()
    {
        foreach (var e in Entries)
        {
            e.ErrorText = null;
            e.Record.WantsToRun = true;
        }
        SaveSession();
        await TickAsync();
    }

    private static async Task StopQuietlyAsync(TorrentEntry entry)
    {
        if (entry.Manager.State is TorrentState.Stopped or TorrentState.Stopping or TorrentState.Error) return;
        try { await entry.Manager.StopAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception ex) { Log.Warn($"stop {entry.Name}: {ex.Message}"); }
    }

    // ------------------------------------------------------------------ settings

    public async Task ApplySettingsAsync(AppSettings next)
    {
        await gate.WaitAsync();
        try
        {
            var boostTurnedOn = next.TrackerBoost && !Settings.TrackerBoost;
            Settings = next.Normalised();
            Settings.Save();
            appliedLimits = (-1, -1);
            await engine.UpdateSettingsAsync(BuildEngineSettings(Settings, DateTime.Now));
            foreach (var e in Entries)
            {
                await e.Manager.UpdateSettingsAsync(BuildTorrentSettings(e.Record));
                if (boostTurnedOn) await BoostAsync(e);
            }
        }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Holds torrent traffic to a trickle while the speed test runs, so the test measures
    /// the line and not the line minus whatever the torrents were using.
    /// </summary>
    public async Task SuspendTrafficAsync(bool suspend)
    {
        limitsSuspended = suspend;
        appliedLimits = (-1, -1);
        await ApplyLimitsAsync();
    }

    private async Task ApplyLimitsAsync()
    {
        var now = DateTime.Now;
        SlowActive = SpeedSchedule.SlowNow(Settings, now);
        var limits = limitsSuspended ? (1024, 1024) : SpeedSchedule.EffectiveLimits(Settings, now);
        if (limits == appliedLimits) return;
        appliedLimits = limits;
        var b = new EngineSettingsBuilder(engine.Settings)
        {
            MaximumDownloadRate = limits.Item1,
            MaximumUploadRate = limits.Item2,
        };
        await engine.UpdateSettingsAsync(b.ToSettings());
    }

    /// <summary>Every interface by default. The loopback test binds to 127.0.0.1 so it never meets a firewall prompt.</summary>
    internal static IPAddress BindAddress { get; set; } = IPAddress.Any;

    internal static EngineSettings BuildEngineSettings(AppSettings s, DateTime now)
    {
        var (down, up) = SpeedSchedule.EffectiveLimits(s, now);
        var port = s.ListenPort;
        var bind6 = IPAddress.IsLoopback(BindAddress) ? IPAddress.IPv6Loopback : IPAddress.IPv6Any;
        var b = new EngineSettingsBuilder
        {
            CacheDirectory = AppPaths.CacheDir,
            AutoSaveLoadFastResume = true,
            AutoSaveLoadMagnetLinkMetadata = true,
            AutoSaveLoadDhtCache = true,
            FastResumeMode = FastResumeMode.BestEffort,
            AllowPortForwarding = s.PortForwarding,
            AllowLocalPeerDiscovery = s.LocalPeerDiscovery,
            MaximumConnections = s.MaxConnections,
            MaximumHalfOpenConnections = s.MaxHalfOpen,
            MaximumDownloadRate = down,
            MaximumUploadRate = up,
            DiskCacheBytes = s.DiskCacheMiB * 1024 * 1024,
            MaximumOpenFiles = 64,
            // Real file names from the first byte, so a video can be opened while it downloads.
            UsePartialFiles = false,
            ListenEndPoints = new Dictionary<string, IPEndPoint>
            {
                ["ipv4"] = new(BindAddress, port),
                ["ipv6"] = new(bind6, port),
            },
            DhtEndPoint = s.Dht ? new IPEndPoint(BindAddress, port) : null,
            AllowedEncryption = s.Encryption switch
            {
                EncryptionMode.Require => [EncryptionType.RC4Full, EncryptionType.RC4Header],
                EncryptionMode.Allow => [EncryptionType.PlainText, EncryptionType.RC4Header, EncryptionType.RC4Full],
                _ => [EncryptionType.RC4Header, EncryptionType.RC4Full, EncryptionType.PlainText],
            },
        };
        return b.ToSettings();
    }

    private TorrentSettings BuildTorrentSettings(TorrentRecord r) => new TorrentSettingsBuilder
    {
        MaximumConnections = Settings.MaxConnectionsPerTorrent,
        MaximumDownloadRate = r.DownLimitKiB * 1024,
        MaximumUploadRate = r.UpLimitKiB * 1024,
        AllowDht = Settings.Dht,
        AllowPeerExchange = true,
        CreateContainingDirectory = true,
    }.ToSettings();

    // ------------------------------------------------------------------ the tick

    /// <summary>Once a second: counters, completion, ratio, queue, and a save now and then.</summary>
    public async Task TickAsync()
    {
        if (!await gate.WaitAsync(0)) return; // the previous tick is still busy, skip this one
        try
        {
            await ApplyLimitsAsync();
            History.Add(DownRate, UpRate);

            foreach (var e in Entries)
            {
                Count(e);
                e.History.Add(e.DownRate, e.UpRate);
                await CatchMetadataAsync(e);
                AdvanceSequencer(e);

                if (e.IsComplete && e.Record.CompletedUtc == null && e.WantedSize > 0)
                {
                    e.Record.CompletedUtc = DateTime.UtcNow;
                    SaveSession();
                    Log.Info($"completed {e.Name}");
                    Completed?.Invoke(e);
                }

                if (Settings.SeedRatioLimit > 0 && e.IsComplete && e.Record.WantsToRun && !e.Record.Forced
                    && e.Ratio >= Settings.SeedRatioLimit)
                {
                    e.Record.WantsToRun = false;
                    await StopQuietlyAsync(e);
                    Log.Info($"{e.Name} reached ratio {e.Ratio:0.00}, stopped seeding");
                }
            }

            await RunQueueAsync();

            if (++ticks % 30 == 0) SaveSession();
        }
        catch (Exception ex) { Log.Error("tick", ex); }
        finally { gate.Release(); }
    }

    private static void Count(TorrentEntry e)
    {
        var received = e.Manager.Monitor.DataBytesReceived;
        var sent = e.Manager.Monitor.DataBytesSent;
        if (received < e.LastReceived) e.LastReceived = 0;
        if (sent < e.LastSent) e.LastSent = 0;
        e.Record.Downloaded += received - e.LastReceived;
        e.Record.Uploaded += sent - e.LastSent;
        e.LastReceived = received;
        e.LastSent = sent;
    }

    /// <summary>When a magnet's metadata lands, keep a copy so the next start does not need the swarm for it.</summary>
    private async Task CatchMetadataAsync(TorrentEntry e)
    {
        if (e.Record.TorrentFile != null || !e.HasMetadata) return;
        try
        {
            var target = Path.Combine(AppPaths.TorrentsDir, e.Id + ".torrent");
            if (e.Manager.MetadataPath is { } src && File.Exists(src))
            {
                File.Copy(src, target, overwrite: true);
                e.Record.TorrentFile = target;
            }
            e.Record.Name = e.Name;
            await BoostAsync(e);
            SaveSession();
        }
        catch (IOException ex) { Log.Warn($"metadata copy for {e.Name}: {ex.Message}"); }
    }

    /// <summary>
    /// The streaming picker downloads a window ahead of a read position. Moving that position
    /// to the first missing piece of the first unfinished file turns it into front to back
    /// downloading, which is what lets a video play before it finishes.
    /// </summary>
    private static void AdvanceSequencer(TorrentEntry e)
    {
        if (e.Sequencer == null || !e.HasMetadata || e.Manager.State != TorrentState.Downloading) return;
        var bitfield = e.Manager.Bitfield;
        var pieceLength = (long)e.Manager.Torrent!.PieceLength;
        foreach (var file in e.Manager.Files)
        {
            if (file.Priority == Priority.DoNotDownload || file.Length == 0) continue;
            for (var i = file.StartPieceIndex; i <= file.EndPieceIndex; i++)
            {
                if (bitfield[i]) continue;
                var position = Math.Clamp(i * pieceLength - file.OffsetInTorrent, 0, file.Length - 1);
                e.Sequencer.SeekToPosition(file, position);
                return;
            }
        }
    }

    private async Task RunQueueAsync()
    {
        var all = Entries.Where(e => e.ErrorText == null && !e.Moving && e.Manager.State != TorrentState.Stopping).ToList();
        var (start, hold) = QueuePolicy.Decide(
            all.Select(e => new QueueEntry(e.Id, e.Record.WantsToRun, e.IsComplete, e.IsRunning, e.Record.AddedUtc, e.Record.Forced)),
            Settings.MaxActiveDownloads);

        foreach (var id in start)
        {
            var e = all.First(x => x.Id == id);
            if (e.Manager.State is TorrentState.Stopped or TorrentState.Paused)
            {
                try { await e.Manager.StartAsync(); }
                catch (Exception ex)
                {
                    e.ErrorText = ex.Message;
                    Log.Error($"start {e.Name}", ex);
                }
            }
        }
        foreach (var id in hold)
            await StopQuietlyAsync(all.First(x => x.Id == id));
    }

    // ------------------------------------------------------------------ helpers

    public void SaveSession()
    {
        try
        {
            List<TorrentRecord> records;
            lock (entries) records = entries.Select(e => e.Record).ToList();
            SessionStore.Save(records);
        }
        catch (IOException ex) { Log.Error("session save", ex); }
    }

    public static string IdOf(InfoHashes hashes) => hashes.V1OrV2.ToHex().ToLowerInvariant();
}
