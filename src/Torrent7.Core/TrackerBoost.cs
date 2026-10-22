namespace Torrent7.Core;

/// <summary>
/// Adds well run public trackers to public torrents so they find more peers sooner. Private
/// torrents are never touched: a private tracker counts your ratio, and announcing its
/// torrent anywhere else breaks its rules and can get the account banned.
/// </summary>
public static class TrackerBoost
{
    // Long lived public trackers. The list refreshes from the community maintained
    // trackerslist project once a day; this is the fallback when that cannot be reached.
    public static readonly IReadOnlyList<string> BuiltIn =
    [
        "udp://tracker.opentrackr.org:1337/announce",
        "udp://open.demonii.com:1337/announce",
        "udp://open.stealth.si:80/announce",
        "udp://tracker.torrent.eu.org:451/announce",
        "udp://exodus.desync.com:6969/announce",
        "udp://explodie.org:6969/announce",
        "udp://tracker.dler.org:6969/announce",
        "udp://opentracker.io:6969/announce",
        "udp://tracker.tiny-vps.com:6969/announce",
        "udp://tracker.openbittorrent.com:6969/announce",
        "http://tracker.opentrackr.org:1337/announce",
    ];

    private const string RemoteList = "https://cdn.jsdelivr.net/gh/ngosang/trackerslist@master/trackers_best.txt";

    private static IReadOnlyList<string>? cached;

    public static IReadOnlyList<string> Current => cached ??= LoadCached() ?? BuiltIn;

    /// <summary>Fetches the community list if the copy on disk is more than a day old. Never throws.</summary>
    public static async Task RefreshAsync(HttpClient http, CancellationToken token = default)
    {
        try
        {
            var file = new FileInfo(AppPaths.TrackerListFile);
            if (file.Exists && DateTime.UtcNow - file.LastWriteTimeUtc < TimeSpan.FromDays(1))
                return;

            var text = await http.GetStringAsync(RemoteList, token).ConfigureAwait(false);
            var parsed = Parse(text);
            if (parsed.Count < 5) return; // a truncated or captive portal reply, keep what we have

            Directory.CreateDirectory(AppPaths.DataDir);
            AtomicFile.WriteAllText(AppPaths.TrackerListFile, string.Join('\n', parsed));
            cached = Merge(parsed, BuiltIn);
            Log.Info($"tracker list refreshed, {parsed.Count} trackers");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Warn("tracker list refresh failed: " + ex.Message);
        }
    }

    /// <summary>One announce URL per line, comments and anything that is not a tracker dropped.</summary>
    public static List<string> Parse(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith('#'))
            .Where(l => Uri.TryCreate(l, UriKind.Absolute, out var u) && u.Scheme is "udp" or "http" or "https")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The trackers worth adding: ones the torrent does not already announce to.</summary>
    public static IEnumerable<string> Missing(IEnumerable<string> existing, IEnumerable<string> boost)
    {
        var have = existing.Select(Normalise).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var url in boost)
            if (have.Add(Normalise(url)))
                yield return url;
    }

    private static string Normalise(string url) => url.Trim().TrimEnd('/');

    private static IReadOnlyList<string> Merge(IEnumerable<string> a, IEnumerable<string> b) =>
        a.Concat(b).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static IReadOnlyList<string>? LoadCached()
    {
        try
        {
            if (!File.Exists(AppPaths.TrackerListFile)) return null;
            var parsed = Parse(File.ReadAllText(AppPaths.TrackerListFile));
            return parsed.Count >= 5 ? Merge(parsed, BuiltIn) : null;
        }
        catch (IOException) { return null; }
    }
}
