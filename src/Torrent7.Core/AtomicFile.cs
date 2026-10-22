namespace Torrent7.Core;

internal static class AtomicFile
{
    /// <summary>
    /// Writes beside the target and swaps it in, so a crash or a power cut mid write leaves
    /// the previous file rather than half of a new one.
    /// </summary>
    public static void WriteAllText(string path, string contents)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }
}
