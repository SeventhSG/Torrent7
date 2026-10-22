namespace Torrent7.Core;

/// <summary>One torrent as the queue sees it.</summary>
public readonly record struct QueueEntry(string Id, bool WantsToRun, bool Complete, bool Running, DateTime AddedUtc, bool Forced);

/// <summary>
/// Keeps at most N downloads moving at once. Too many parallel downloads split the line so
/// thinly that none of them finishes, and every one of them holds connections and file
/// handles. Seeding and forced torrents do not count against the limit.
/// </summary>
public static class QueuePolicy
{
    public static (IReadOnlyList<string> Start, IReadOnlyList<string> Hold) Decide(IEnumerable<QueueEntry> entries, int maxActiveDownloads)
    {
        var list = entries.ToList();
        var start = new List<string>();
        var hold = new List<string>();

        foreach (var e in list.Where(e => e.WantsToRun && (e.Complete || e.Forced) && !e.Running))
            start.Add(e.Id);

        var downloads = list.Where(e => e.WantsToRun && !e.Complete && !e.Forced)
                            .OrderBy(e => e.AddedUtc)
                            .ToList();

        // Running ones keep their slot first, so the queue never flips a torrent that is
        // already moving in favour of one that is only waiting.
        var slots = Math.Max(1, maxActiveDownloads);
        var keep = downloads.Where(e => e.Running).Take(slots).Select(e => e.Id).ToHashSet();
        foreach (var e in downloads.Where(e => !e.Running))
        {
            if (keep.Count >= slots) break;
            keep.Add(e.Id);
            start.Add(e.Id);
        }
        foreach (var e in downloads.Where(e => e.Running && !keep.Contains(e.Id)))
            hold.Add(e.Id);

        return (start, hold);
    }
}
