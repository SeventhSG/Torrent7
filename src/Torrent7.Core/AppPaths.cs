namespace Torrent7.Core;

/// <summary>
/// Where Torrent7 keeps its own files. A file named <c>portable</c> next to the executable
/// keeps everything beside it instead, so the app runs from a USB stick without leaving
/// anything behind on the machine.
/// </summary>
public static class AppPaths
{
    public static string DataDir { get; private set; } = ResolveDataDir();

    public static bool IsPortable { get; private set; }

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string SessionFile => Path.Combine(DataDir, "session.json");
    public static string TorrentsDir => Path.Combine(DataDir, "torrents");
    public static string CacheDir => Path.Combine(DataDir, "cache");
    public static string LogFile => Path.Combine(DataDir, "torrent7.log");
    public static string TrackerListFile => Path.Combine(DataDir, "trackers.txt");

    /// <summary>Points everything at another folder. Tests use this to stay off the real profile.</summary>
    public static void UseDataDir(string dir)
    {
        DataDir = dir;
        Directory.CreateDirectory(dir);
    }

    public static string DefaultDownloadDir
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsLinux())
            {
                // XDG names the folder per locale, so ask for it before guessing.
                var xdg = ReadXdgDownloadDir(home);
                if (xdg != null) return xdg;
            }
            return Path.Combine(home, "Downloads");
        }
    }

    private static string ResolveDataDir()
    {
        var exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "portable")) || File.Exists(Path.Combine(exeDir, "portable.txt")))
        {
            IsPortable = true;
            return Path.Combine(exeDir, "Torrent7-data");
        }

        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Torrent7");

        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(config))
            config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(config, "torrent7");
    }

    private static string? ReadXdgDownloadDir(string home)
    {
        try
        {
            var file = Path.Combine(home, ".config", "user-dirs.dirs");
            if (!File.Exists(file)) return null;
            foreach (var line in File.ReadAllLines(file))
            {
                if (!line.StartsWith("XDG_DOWNLOAD_DIR=", StringComparison.Ordinal)) continue;
                var value = line["XDG_DOWNLOAD_DIR=".Length..].Trim().Trim('"').Replace("$HOME", home);
                return value.Length > 0 ? value : null;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }
}
