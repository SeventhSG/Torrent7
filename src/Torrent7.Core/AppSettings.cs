using System.Text.Json;

namespace Torrent7.Core;

public enum EncryptionMode { Prefer, Require, Allow }

/// <summary>
/// Everything the user can change. Limits are KiB/s and 0 means unlimited, the way every
/// field on screen reads them.
/// </summary>
public sealed class AppSettings
{
    public string DownloadDir { get; set; } = AppPaths.DefaultDownloadDir;
    public bool ShowAddDialog { get; set; } = true;
    public bool StartImmediately { get; set; } = true;

    public int ListenPort { get; set; }

    public int DownloadLimitKiB { get; set; }
    public int UploadLimitKiB { get; set; }

    // Slow mode: a second pair of limits, switched by hand or by the schedule.
    public bool SlowMode { get; set; }
    public int SlowDownloadLimitKiB { get; set; } = 2048;
    public int SlowUploadLimitKiB { get; set; } = 256;
    public bool ScheduleEnabled { get; set; }
    public int ScheduleFromMinute { get; set; } = 9 * 60;
    public int ScheduleToMinute { get; set; } = 23 * 60;

    public int MaxConnections { get; set; } = 500;
    public int MaxConnectionsPerTorrent { get; set; } = 150;
    public int MaxHalfOpen { get; set; } = 64;
    public int MaxActiveDownloads { get; set; } = 5;
    public int DiskCacheMiB { get; set; } = 64;

    public bool TrackerBoost { get; set; } = true;
    public bool Dht { get; set; } = true;
    public bool LocalPeerDiscovery { get; set; } = true;
    public bool PortForwarding { get; set; } = true;
    public EncryptionMode Encryption { get; set; } = EncryptionMode.Prefer;

    /// <summary>Stop seeding a finished torrent at this share ratio. 0 keeps seeding.</summary>
    public double SeedRatioLimit { get; set; }

    public string? WatchFolder { get; set; }
    public bool WatchClipboard { get; set; } = true;
    public bool NotifyOnComplete { get; set; } = true;

    /// <summary>Closing the window keeps transfers going from the tray instead of quitting.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Scan each finished download with the system's own virus scanner.</summary>
    public bool ScanDownloads { get; set; } = true;

    /// <summary>Optional. With a key, program files are also looked up on VirusTotal by hash.</summary>
    public string? VirusTotalKey { get; set; }

    /// <summary>What the last speed test measured, in bits per second. 0 until one has run.</summary>
    public long MeasuredDownBps { get; set; }
    public long MeasuredUpBps { get; set; }

    /// <summary>The last few speed test results, newest first.</summary>
    public List<SpeedTestEntry> SpeedTests { get; set; } = [];

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var loaded = JsonSerializer.Deserialize(File.ReadAllText(AppPaths.SettingsFile), JsonContext.Default.AppSettings);
                if (loaded != null) return loaded.Normalised();
            }
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new AppSettings().Normalised();
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        AtomicFile.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, JsonContext.Default.AppSettings));
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(this, JsonContext.Default.AppSettings), JsonContext.Default.AppSettings)!;

    /// <summary>Pulls hand edited or older files back into range.</summary>
    public AppSettings Normalised()
    {
        if (ListenPort is < 1024 or > 65535)
            ListenPort = Random.Shared.Next(49160, 65000);
        if (string.IsNullOrWhiteSpace(DownloadDir))
            DownloadDir = AppPaths.DefaultDownloadDir;
        DownloadLimitKiB = Math.Max(0, DownloadLimitKiB);
        UploadLimitKiB = Math.Max(0, UploadLimitKiB);
        SlowDownloadLimitKiB = Math.Max(0, SlowDownloadLimitKiB);
        SlowUploadLimitKiB = Math.Max(0, SlowUploadLimitKiB);
        MaxConnections = Math.Clamp(MaxConnections, 20, 4000);
        MaxConnectionsPerTorrent = Math.Clamp(MaxConnectionsPerTorrent, 5, MaxConnections);
        MaxHalfOpen = Math.Clamp(MaxHalfOpen, 4, 500);
        MaxActiveDownloads = Math.Clamp(MaxActiveDownloads, 1, 100);
        DiskCacheMiB = Math.Clamp(DiskCacheMiB, 0, 1024);
        ScheduleFromMinute = Math.Clamp(ScheduleFromMinute, 0, 24 * 60 - 1);
        ScheduleToMinute = Math.Clamp(ScheduleToMinute, 0, 24 * 60 - 1);
        SeedRatioLimit = Math.Max(0, SeedRatioLimit);
        return this;
    }
}

public sealed class SpeedTestEntry
{
    public DateTime WhenUtc { get; set; }
    public long DownBps { get; set; }
    public long UpBps { get; set; }
    public double LatencyMs { get; set; }
}
