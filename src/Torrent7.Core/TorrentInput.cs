using System.Text.RegularExpressions;

namespace Torrent7.Core;

public enum TorrentInputKind { None, Magnet, InfoHash, Url, File }

/// <summary>
/// Recognises anything a person might paste or drop: a magnet link, a bare info hash, a
/// link to a .torrent file, or a .torrent on disk. Pure, so the clipboard watcher and the
/// paste box agree on what counts.
/// </summary>
public static partial class TorrentInput
{
    [GeneratedRegex(@"magnet:\?[^\s""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex MagnetPattern();

    [GeneratedRegex(@"^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64}|[A-Za-z2-7]{32})$")]
    private static partial Regex HashPattern();

    public static (TorrentInputKind Kind, string Value) Classify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (TorrentInputKind.None, "");
        var t = text.Trim();

        var magnet = MagnetPattern().Match(t);
        if (magnet.Success && magnet.Value.Contains("xt=urn:bt", StringComparison.OrdinalIgnoreCase))
            return (TorrentInputKind.Magnet, magnet.Value);

        if (HashPattern().IsMatch(t))
        {
            // 64 hex is a v2 (SHA-256) hash, which magnets carry as a multihash with the 1220 prefix.
            var uri = t.Length == 64 ? "magnet:?xt=urn:btmh:1220" + t.ToLowerInvariant()
                                     : "magnet:?xt=urn:btih:" + t;
            return (TorrentInputKind.InfoHash, uri);
        }

        if (Uri.TryCreate(t, UriKind.Absolute, out var u))
        {
            if ((u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) && !t.Contains(' '))
                return (TorrentInputKind.Url, t);
            if (u.IsFile && t.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                return (TorrentInputKind.File, u.LocalPath);
        }

        if (t.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && t.IndexOfAny(Path.GetInvalidPathChars()) < 0)
            return (TorrentInputKind.File, t.Trim('"'));

        return (TorrentInputKind.None, "");
    }

    /// <summary>
    /// The clipboard watcher only offers what is unambiguous: a magnet or a bare hash. A
    /// copied web address is far more often an ordinary page than a .torrent.
    /// </summary>
    public static bool IsWorthOffering(string? text)
    {
        var kind = Classify(text).Kind;
        return kind is TorrentInputKind.Magnet or TorrentInputKind.InfoHash;
    }
}
