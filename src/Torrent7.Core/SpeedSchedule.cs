namespace Torrent7.Core;

/// <summary>Decides which pair of limits applies right now. Pure, so it is tested headless.</summary>
public static class SpeedSchedule
{
    /// <summary>
    /// True when the clock sits inside the slow window. A window that ends earlier than it
    /// starts wraps past midnight, so 22:00 to 07:00 covers the night.
    /// </summary>
    public static bool InWindow(int fromMinute, int toMinute, int nowMinute)
    {
        if (fromMinute == toMinute) return false;
        return fromMinute < toMinute
            ? nowMinute >= fromMinute && nowMinute < toMinute
            : nowMinute >= fromMinute || nowMinute < toMinute;
    }

    public static bool SlowNow(AppSettings s, DateTime localNow) =>
        s.SlowMode || (s.ScheduleEnabled && InWindow(s.ScheduleFromMinute, s.ScheduleToMinute, localNow.Hour * 60 + localNow.Minute));

    /// <summary>The engine wide limits in bytes per second, 0 meaning unlimited.</summary>
    public static (int Down, int Up) EffectiveLimits(AppSettings s, DateTime localNow) =>
        SlowNow(s, localNow)
            ? (s.SlowDownloadLimitKiB * 1024, s.SlowUploadLimitKiB * 1024)
            : (s.DownloadLimitKiB * 1024, s.UploadLimitKiB * 1024);
}
