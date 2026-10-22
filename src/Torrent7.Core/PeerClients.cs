using System.Text;

namespace Torrent7.Core;

/// <summary>
/// Names the program on the other end from its peer id. Most clients use the Azureus style,
/// a dash, two letters and four version characters: -qB4630- is qBittorrent 4.6.3.
/// </summary>
public static class PeerClients
{
    private static readonly Dictionary<string, string> Codes = new()
    {
        ["7T"] = "aTorrent", ["AG"] = "Ares", ["A~"] = "Ares", ["AZ"] = "Vuze", ["BB"] = "BitBuddy",
        ["BC"] = "BitComet", ["BF"] = "Bitflu", ["BI"] = "BiglyBT", ["BT"] = "BitTorrent", ["BW"] = "BitWombat",
        ["CD"] = "Enhanced CTorrent", ["DE"] = "Deluge", ["FD"] = "Free Download Manager", ["FW"] = "FrostWire",
        ["FX"] = "Freebox", ["HL"] = "Halite", ["KG"] = "KGet", ["KT"] = "KTorrent", ["LH"] = "LH-ABC",
        ["LT"] = "libtorrent", ["lt"] = "rTorrent", ["LW"] = "LimeWire", ["MO"] = "MonoTorrent", ["MP"] = "MooPolice",
        ["PI"] = "PicoTorrent", ["qB"] = "qBittorrent", ["QD"] = "QQDownload", ["RT"] = "Retriever",
        ["SB"] = "Swiftbit", ["SD"] = "Thunder", ["SZ"] = "Shareaza", ["TL"] = "Tribler", ["TN"] = "TorrentDotNET",
        ["TR"] = "Transmission", ["TS"] = "Torrentstorm", ["TT"] = "TuoTu", ["UL"] = "uLeecher", ["UM"] = "uTorrent for Mac",
        ["UT"] = "uTorrent", ["UW"] = "uTorrent Web", ["VG"] = "Vagaa", ["WD"] = "WebTorrent Desktop",
        ["WT"] = "BitLet", ["WW"] = "WebTorrent", ["XL"] = "Xunlei", ["XX"] = "Xtorrent", ["ZT"] = "ZipTorrent",
    };

    /// <summary>"qBittorrent 4.6.3", a cleaned up guess, or "Unknown". Never control characters.</summary>
    public static string Name(ReadOnlySpan<byte> peerId, string? fallback)
    {
        if (peerId.Length >= 8 && peerId[0] == '-' && peerId[7] == '-')
        {
            var code = Encoding.ASCII.GetString(peerId.Slice(1, 2));
            var version = Encoding.ASCII.GetString(peerId.Slice(3, 4));
            if (code == "lt" && int.TryParse(version[2..], out var patch))
                return $"rTorrent {version[0]}.{Convert.ToInt32(version[1].ToString(), 16)}.{patch}"; // major, hex minor, two digit patch
            if (Codes.TryGetValue(code, out var name))
                return $"{name} {Version(version)}".TrimEnd();
        }
        var clean = new string((fallback ?? "").Where(c => c >= ' ' && c < 127).ToArray()).Trim('-', ' ');
        return clean.Length is > 0 and <= 24 && clean.All(char.IsLetter) ? clean : "Unknown";
    }

    /// <summary>"4630" reads 4.6.3; letters count as numbers past 9, the way clients use them.</summary>
    private static string Version(string v)
    {
        // A trailing lower case letter is a release tag (Deluge's 220s is 2.2.0 stable), not a number.
        if (v.Length > 0 && char.IsLower(v[^1]) && v[..^1].All(char.IsDigit)) v = v[..^1];
        var parts = v.TrimEnd('0').Select(c => char.IsDigit(c) ? (c - '0').ToString()
                                             : char.IsLetter(c) ? (char.ToUpperInvariant(c) - 'A' + 10).ToString() : "").ToList();
        if (parts.Count == 0) parts.Add("0");
        if (parts.Count == 1) parts.Add("0");
        return string.Join('.', parts);
    }
}
