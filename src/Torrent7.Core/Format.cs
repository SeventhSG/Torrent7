using System.Globalization;

namespace Torrent7.Core;

/// <summary>
/// Sizes and speeds in binary units, labelled as such. Speed tests are the exception and
/// read in megabits, because that is what an internet plan is sold in.
/// </summary>
public static class Format
{
    private static readonly string[] Units = ["B", "KiB", "MiB", "GiB", "TiB"];
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Size(long bytes)
    {
        if (bytes < 1024) return bytes.ToString(Inv) + " B";
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        // Three significant figures at most: 1.23 GiB, 12.3 GiB, 123 GiB.
        var text = value >= 100 ? value.ToString("0", Inv)
                 : value >= 10 ? value.ToString("0.0", Inv)
                 : value.ToString("0.00", Inv);
        return text + " " + Units[unit];
    }

    public static string Speed(long bytesPerSecond) =>
        bytesPerSecond <= 0 ? "0 B/s" : Size(bytesPerSecond) + "/s";

    public static string Megabits(long bitsPerSecond)
    {
        var mbps = bitsPerSecond / 1_000_000.0;
        return (mbps >= 100 ? mbps.ToString("0", Inv) : mbps.ToString("0.0", Inv)) + " Mbps";
    }

    public static string Limit(int kib) => kib <= 0 ? "Unlimited" : Speed(kib * 1024L);

    /// <summary>Two most significant units only. Nobody plans around the seconds of a three hour wait.</summary>
    public static string Eta(TimeSpan? eta)
    {
        if (eta is not { } t || t < TimeSpan.Zero) return "";
        if (t.TotalDays >= 100) return "over 100 d";
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays} d {t.Hours} h";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes} min";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes} min {t.Seconds} s";
        return $"{Math.Max(1, t.Seconds)} s";
    }

    /// <summary>Time left at the current rate, or null when nothing is moving.</summary>
    public static TimeSpan? EtaFor(long remainingBytes, long bytesPerSecond)
    {
        if (remainingBytes <= 0) return TimeSpan.Zero;
        if (bytesPerSecond <= 0) return null;
        return TimeSpan.FromSeconds(remainingBytes / (double)bytesPerSecond);
    }

    public static string Percent(double progress0To100) =>
        progress0To100 >= 100 ? "100%" : progress0To100.ToString("0.0", Inv) + "%";

    public static string Ratio(double ratio) =>
        double.IsFinite(ratio) ? ratio.ToString("0.00", Inv) : "inf";

    public static string ClockMinute(int minuteOfDay) =>
        $"{minuteOfDay / 60:00}:{minuteOfDay % 60:00}";

    public static bool TryParseClock(string text, out int minuteOfDay)
    {
        minuteOfDay = 0;
        var parts = text.Trim().Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], NumberStyles.None, Inv, out var h) || h is < 0 or > 23) return false;
        if (!int.TryParse(parts[1], NumberStyles.None, Inv, out var m) || m is < 0 or > 59) return false;
        minuteOfDay = h * 60 + m;
        return true;
    }
}
