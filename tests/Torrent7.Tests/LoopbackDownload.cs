using System.Net;
using System.Net.Sockets;
using MonoTorrent;
using MonoTorrent.Client;
using Torrent7.Core;

namespace Torrent7.Tests;

/// <summary>
/// A real download between two engines on this machine: a plain MonoTorrent engine seeds a
/// torrent made here, and Torrent7's service fetches it over 127.0.0.1. Nothing leaves the
/// machine: no trackers, DHT, local discovery or port forwarding, and the files are random
/// bytes made for the test. One file is left unticked, to prove file selection holds.
/// </summary>
internal static class LoopbackDownload
{
    public static async Task RunAsync(Action<string, bool, string> check, string root)
    {
        var seedDir = Path.Combine(root, "seed");
        var leechDir = Path.Combine(root, "leech");
        var content = Path.Combine(seedDir, "Loopback");
        Directory.CreateDirectory(content);
        Directory.CreateDirectory(leechDir);

        var big = new byte[6 * 1024 * 1024 + 12345];
        var small = new byte[300 * 1024];
        Random.Shared.NextBytes(big);
        Random.Shared.NextBytes(small);
        await File.WriteAllBytesAsync(Path.Combine(content, "big.bin"), big);
        await File.WriteAllBytesAsync(Path.Combine(content, "skip-me.bin"), small);

        var creator = new TorrentCreator(TorrentType.V1Only) { PieceLength = 256 * 1024, Comment = "Torrent7 loopback test" };
        var dict = await creator.CreateAsync(new TorrentFileSource(content));
        var bytes = dict.Encode();
        var torrent = Torrent.Load(bytes);

        var seedPort = FreePort();
        var leechPort = FreePort();

        var seedSettings = new EngineSettingsBuilder
        {
            AllowPortForwarding = false,
            AllowLocalPeerDiscovery = false,
            AutoSaveLoadDhtCache = false,
            AutoSaveLoadFastResume = false,
            CacheDirectory = Path.Combine(root, "seed-cache"),
            DhtEndPoint = null,
            ListenEndPoints = new() { ["ipv4"] = new IPEndPoint(IPAddress.Loopback, seedPort) },
        }.ToSettings();
        using var seeder = new ClientEngine(seedSettings);
        var seed = await seeder.AddAsync(torrent, seedDir);
        await seed.HashCheckAsync(autoStart: true);
        check("loopback: the seeder has the whole torrent", seed.Complete, $"seeder progress {seed.Progress:0.0}%");

        TorrentService.BindAddress = IPAddress.Loopback;
        var settings = new AppSettings
        {
            DownloadDir = leechDir,
            ListenPort = leechPort,
            Dht = false,
            LocalPeerDiscovery = false,
            PortForwarding = false,
            TrackerBoost = false,
        }.Normalised();
        using var http = new HttpClient();
        await using var service = new TorrentService(settings, http);
        await service.StartAsync();

        var completed = false;
        service.Completed += _ => completed = true;

        var entry = await service.AddAsync(new AddRequest(bytes, null, leechDir, Start: true,
            SkipFiles: new HashSet<string> { "skip-me.bin" }));
        check("loopback: the add lands in the list", service.Entries.Count == 1, $"{service.Entries.Count} entries");

        var skipped = entry.Manager.Files.First(f => f.Path == "skip-me.bin");
        check("loopback: an unticked file is set to skip", skipped.Priority == Priority.DoNotDownload, $"priority {skipped.Priority}");

        // Sequential mode must switch both ways without disturbing anything.
        await service.SetSequentialAsync(entry, true);
        await service.SetSequentialAsync(entry, false);

        var deadline = DateTime.UtcNow.AddSeconds(45);
        var peerAdded = false;
        while (!completed && DateTime.UtcNow < deadline)
        {
            await service.TickAsync();
            if (!peerAdded && entry.Manager.State == TorrentState.Downloading)
            {
                await entry.Manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{seedPort}")));
                peerAdded = true;
            }
            await Task.Delay(250);
        }

        check("loopback: the download finishes", completed, $"state {entry.Manager.State}, {entry.Progress:0.0}%");
        check("loopback: finished is recorded", entry.Record.CompletedUtc != null, "no completion time");

        // Flush to disk before reading the file back.
        await service.PauseAsync(entry);
        var got = Path.Combine(leechDir, "Loopback", "big.bin");
        var same = File.Exists(got) && (await File.ReadAllBytesAsync(got)).AsSpan().SequenceEqual(big);
        check("loopback: the downloaded file is byte for byte the original", same, File.Exists(got) ? "content differs" : "file missing");
        check("loopback: the downloaded bytes are counted", entry.Record.Downloaded >= big.Length, $"counted {entry.Record.Downloaded}");
        check("loopback: pause stops it", !entry.IsRunning && entry.Status == EntryStatus.Done, $"status {entry.Status}, state {entry.Manager.State}");

        var session = await File.ReadAllTextAsync(AppPaths.SessionFile);
        check("loopback: the session file remembers the torrent", session.Contains(entry.Id), "id not in session.json");

        await service.RemoveAsync(entry, deleteFiles: false);
        check("loopback: remove keeps the files when asked to", File.Exists(got), "file was deleted");
        check("loopback: remove takes it off the list", service.Entries.Count == 0, $"{service.Entries.Count} entries left");

        await seeder.StopAllAsync();
        TorrentService.BindAddress = IPAddress.Any;
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
