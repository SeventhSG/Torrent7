using System.Text.Json;
using MonoTorrent;

namespace Torrent7.Core;

/// <summary>What Torrent7 remembers about one torrent between runs. The engine's fast resume
/// data covers which pieces are on disk; this covers what the user asked for.</summary>
public sealed class TorrentRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? TorrentFile { get; set; }
    public string? Magnet { get; set; }
    public string SaveDir { get; set; } = "";

    /// <summary>The user's intent. The queue decides whether it is actually moving.</summary>
    public bool WantsToRun { get; set; } = true;
    public bool Forced { get; set; }
    public bool Sequential { get; set; }
    public int DownLimitKiB { get; set; }
    public int UpLimitKiB { get; set; }

    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }

    /// <summary>All time payload bytes, so the ratio survives a restart.</summary>
    public long Downloaded { get; set; }
    public long Uploaded { get; set; }

    /// <summary>Files the user changed from Normal, keyed by their path inside the torrent.</summary>
    public Dictionary<string, Priority> FilePriorities { get; set; } = [];
}

internal static class SessionStore
{
    public static List<TorrentRecord> Load()
    {
        try
        {
            if (File.Exists(AppPaths.SessionFile))
                return JsonSerializer.Deserialize(File.ReadAllText(AppPaths.SessionFile), JsonContext.Default.ListTorrentRecord) ?? [];
        }
        catch (JsonException ex)
        {
            // Keep the unreadable file for the user rather than overwriting their list on next save.
            Log.Error("session.json unreadable, kept as session.json.bad", ex);
            try { File.Copy(AppPaths.SessionFile, AppPaths.SessionFile + ".bad", overwrite: true); } catch (IOException) { }
        }
        catch (IOException ex) { Log.Error("session.json unreadable", ex); }
        return [];
    }

    private static readonly object Gate = new();

    public static void Save(IEnumerable<TorrentRecord> records)
    {
        var json = JsonSerializer.Serialize(records.ToList(), JsonContext.Default.ListTorrentRecord);
        // Several torrents can be paused at once, and each saves; one writer at a time.
        lock (Gate)
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            AtomicFile.WriteAllText(AppPaths.SessionFile, json);
        }
    }
}
