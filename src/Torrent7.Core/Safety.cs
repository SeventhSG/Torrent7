using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Torrent7.Core;

public enum ScanVerdict { Clean, Threat, Unavailable, Failed }

public sealed record ScanResult(ScanVerdict Verdict, string Message);

/// <summary>
/// Checks downloads before anyone opens them. The local scanner does the real work: Windows
/// Defender on Windows, ClamAV on Linux, both of which look at the files on this machine and
/// send nothing anywhere. VirusTotal is optional and only ever sees fingerprints (SHA-256
/// hashes) of program files, never the files themselves.
/// </summary>
public static class Safety
{
    /// <summary>Files that run code when opened. These are what malware in torrents hides in.</summary>
    private static readonly HashSet<string> Programs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".bat", ".cmd", ".com", ".scr", ".pif", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf",
        ".hta", ".jar", ".lnk", ".dll", ".reg", ".cpl", ".msc", ".apk", ".sh", ".appimage", ".deb", ".rpm", ".run",
    };

    private static readonly HashSet<string> Media = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".mp3", ".flac", ".wav", ".jpg", ".png", ".pdf", ".epub", ".txt", ".srt",
    };

    public static bool IsProgram(string path) => Programs.Contains(Path.GetExtension(path));

    /// <summary>"movie.mp4.exe": a program dressed up as something harmless, the oldest trick there is.</summary>
    public static bool IsDisguised(string path) =>
        IsProgram(path) && Media.Contains(Path.GetExtension(Path.GetFileNameWithoutExtension(path)));

    /// <summary>Runs the local scanner over a finished download. Never throws.</summary>
    public static async Task<ScanResult> ScanLocalAsync(string path, CancellationToken token = default)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var defender = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
                if (!File.Exists(defender)) return new(ScanVerdict.Unavailable, "Windows Defender is not available on this PC.");
                // Exit code 2 means Defender found something; it quarantines it itself.
                var code = await RunAsync(defender, $"-Scan -ScanType 3 -File \"{path}\"", token);
                return code switch
                {
                    0 => new(ScanVerdict.Clean, "Windows Defender found nothing."),
                    2 => new(ScanVerdict.Threat, "Windows Defender found a threat and quarantined it."),
                    _ => new(ScanVerdict.Failed, $"Windows Defender could not finish (code {code})."),
                };
            }
            if (OperatingSystem.IsLinux())
            {
                var code = await RunAsync("clamscan", $"-r --no-summary --infected \"{path}\"", token);
                return code switch
                {
                    0 => new(ScanVerdict.Clean, "ClamAV found nothing."),
                    1 => new(ScanVerdict.Threat, "ClamAV found a threat. The file was left alone; delete it."),
                    -1 => new(ScanVerdict.Unavailable, "Install ClamAV (clamscan) to scan downloads."),
                    _ => new(ScanVerdict.Failed, $"ClamAV could not finish (code {code})."),
                };
            }
            return new(ScanVerdict.Unavailable, "No virus scanner on this system.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new(ScanVerdict.Failed, "The scan failed: " + ex.Message); }
    }

    /// <summary>
    /// Looks up the program files of a download by hash. Only hashes leave the machine, and
    /// only for programs: a hash of a film says nothing useful and costs a lookup.
    /// </summary>
    public static async Task<ScanResult> CheckVirusTotalAsync(HttpClient http, string apiKey, IEnumerable<string> files, CancellationToken token = default)
    {
        var programs = files.Where(IsProgram).Take(4).ToList(); // the free key allows four lookups a minute
        if (programs.Count == 0) return new(ScanVerdict.Clean, "No programs to look up.");
        var flagged = new List<string>();
        foreach (var file in programs)
        {
            string hash;
            await using (var s = File.OpenRead(file))
                hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(s, token));
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://www.virustotal.com/api/v3/files/{hash}");
            req.Headers.Add("x-apikey", apiKey);
            using var resp = await http.SendAsync(req, token);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) continue; // never seen before: nothing to report
            if (!resp.IsSuccessStatusCode) return new(ScanVerdict.Failed, $"VirusTotal answered {(int)resp.StatusCode}.");
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(token));
            var stats = doc.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("last_analysis_stats");
            var malicious = stats.GetProperty("malicious").GetInt32();
            if (malicious > 0) flagged.Add($"{Path.GetFileName(file)} ({malicious} engines)");
        }
        return flagged.Count == 0
            ? new(ScanVerdict.Clean, "VirusTotal knows no problem with its programs.")
            : new(ScanVerdict.Threat, "VirusTotal flags " + string.Join(", ", flagged) + ".");
    }

    private static async Task<int> RunAsync(string file, string args, CancellationToken token)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
            if (p == null) return -1;
            _ = p.StandardOutput.ReadToEndAsync(token);
            await p.WaitForExitAsync(token);
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception) { return -1; } // not installed
    }
}
