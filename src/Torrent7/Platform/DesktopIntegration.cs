using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Torrent7.Core;

namespace Torrent7.Platform;

/// <summary>
/// Makes magnet links and .torrent files open in Torrent7. Only ever runs from the button in
/// Settings, and only writes to the current user's part of the system.
/// </summary>
public static class DesktopIntegration
{
    public static string Register()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot tell where Torrent7 is running from.");
        if (OperatingSystem.IsWindows()) return RegisterWindows(exe);
        if (OperatingSystem.IsLinux()) return RegisterLinux(exe);
        return "Not supported on this system.";
    }

    /// <summary>Takes back everything <see cref="Register"/> wrote, and nothing it did not.</summary>
    public static string Unregister()
    {
        if (OperatingSystem.IsWindows()) return UnregisterWindows();
        if (OperatingSystem.IsLinux()) return UnregisterLinux();
        return "Not supported on this system.";
    }

    [SupportedOSPlatform("windows")]
    private static string UnregisterWindows()
    {
        using (var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true))
        {
            if (classes != null)
            {
                classes.DeleteSubKeyTree("Torrent7.magnet", throwOnMissingSubKey: false);
                classes.DeleteSubKeyTree("Torrent7.torrent", throwOnMissingSubKey: false);

                // The plain magnet: key only goes if it is still ours.
                var current = classes.OpenSubKey(@"magnet\shell\open\command")?.GetValue("") as string;
                if (current != null && current.Contains("Torrent7", StringComparison.OrdinalIgnoreCase))
                    classes.DeleteSubKeyTree("magnet", throwOnMissingSubKey: false);

                var leftovers = 0;
                using (var ext = classes.OpenSubKey(".torrent", writable: true))
                {
                    if (ext != null)
                    {
                        using (var open = ext.OpenSubKey("OpenWithProgids", writable: true))
                        {
                            open?.DeleteValue("Torrent7.torrent", throwOnMissingValue: false);
                            leftovers += open?.ValueCount ?? 0;
                        }
                        if (ext.GetValue("") as string == "Torrent7.torrent") ext.DeleteValue("", throwOnMissingValue: false);
                        leftovers += ext.ValueCount + ext.GetSubKeyNames().Count(n => n != "OpenWithProgids");
                    }
                }
                // Drop the .torrent key only when nothing but our own entries was ever in it.
                if (leftovers == 0) classes.DeleteSubKeyTree(".torrent", throwOnMissingSubKey: false);
            }
        }
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Torrent7", throwOnMissingSubKey: false);
        using (var reg = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true))
            reg?.DeleteValue("Torrent7", throwOnMissingValue: false);

        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        Log.Info("removed magnet and .torrent handlers for the current user");
        return "Removed. Magnet links and .torrent files no longer open Torrent7.";
    }

    private static string UnregisterLinux()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome)) dataHome = Path.Combine(home, ".local", "share");
        var apps = Path.Combine(dataHome, "applications");
        File.Delete(Path.Combine(apps, "torrent7.desktop"));
        File.Delete(Path.Combine(dataHome, "icons", "hicolor", "256x256", "apps", "torrent7.png"));
        TryRun("update-desktop-database", $"\"{apps}\"");
        Log.Info("removed torrent7.desktop");
        return "Removed from the applications menu.";
    }

    [SupportedOSPlatform("windows")]
    private static string RegisterWindows(string exe)
    {
        var command = $"\"{exe}\" \"%1\"";
        var icon = $"\"{exe}\",0";

        using (var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes"))
        {
            using (var magnet = classes.CreateSubKey("Torrent7.magnet"))
            {
                magnet.SetValue("", "Magnet link");
                magnet.SetValue("URL Protocol", "");
                using (var k = magnet.CreateSubKey("DefaultIcon")) k.SetValue("", icon);
                using (var k = magnet.CreateSubKey(@"shell\open\command")) k.SetValue("", command);
            }
            using (var file = classes.CreateSubKey("Torrent7.torrent"))
            {
                file.SetValue("", "Torrent file");
                using (var k = file.CreateSubKey("DefaultIcon")) k.SetValue("", icon);
                using (var k = file.CreateSubKey(@"shell\open\command")) k.SetValue("", command);
            }

            // The plain magnet: key is what browsers look up. Only claim it when nothing else has.
            using (var magnetRoot = classes.CreateSubKey("magnet"))
            {
                var current = magnetRoot.OpenSubKey(@"shell\open\command")?.GetValue("") as string;
                if (string.IsNullOrEmpty(current) || current.Contains("Torrent7", StringComparison.OrdinalIgnoreCase))
                {
                    magnetRoot.SetValue("", "URL:Magnet link");
                    magnetRoot.SetValue("URL Protocol", "");
                    using (var k = magnetRoot.CreateSubKey("DefaultIcon")) k.SetValue("", icon);
                    using (var k = magnetRoot.CreateSubKey(@"shell\open\command")) k.SetValue("", command);
                }
            }
            using (var ext = classes.CreateSubKey(".torrent"))
            {
                using var open = ext.CreateSubKey("OpenWithProgids");
                open.SetValue("Torrent7.torrent", Array.Empty<byte>(), RegistryValueKind.None);
                if (string.IsNullOrEmpty(ext.GetValue("") as string)) ext.SetValue("", "Torrent7.torrent");
            }
        }

        // Listing under Default apps, so the user can make the final choice where Windows wants it made.
        using (var caps = Registry.CurrentUser.CreateSubKey(@"Software\Torrent7\Capabilities"))
        {
            caps.SetValue("ApplicationName", "Torrent7");
            caps.SetValue("ApplicationDescription", "A lightweight, fast torrent client.");
            using (var k = caps.CreateSubKey("URLAssociations")) k.SetValue("magnet", "Torrent7.magnet");
            using (var k = caps.CreateSubKey("FileAssociations")) k.SetValue(".torrent", "Torrent7.torrent");
        }
        using (var reg = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            reg.SetValue("Torrent7", @"Software\Torrent7\Capabilities");

        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        Log.Info("registered magnet and .torrent handlers for the current user");
        return "Done. If another app still opens them, choose Torrent7 under Settings, Apps, Default apps.";
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    private static string RegisterLinux(string exe)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome)) dataHome = Path.Combine(home, ".local", "share");
        var apps = Path.Combine(dataHome, "applications");
        Directory.CreateDirectory(apps);

        // The icon ships inside the executable, so write a copy where the desktop can find it.
        var iconDir = Path.Combine(dataHome, "icons", "hicolor", "256x256", "apps");
        Directory.CreateDirectory(iconDir);
        var iconPath = Path.Combine(iconDir, "torrent7.png");
        using (var src = Avalonia.Platform.AssetLoader.Open(new Uri("avares://Torrent7/Assets/Torrent7-256.png")))
        using (var dst = File.Create(iconPath))
            src.CopyTo(dst);

        var desktop = $"""
            [Desktop Entry]
            Type=Application
            Name=Torrent7
            Comment=A lightweight, fast torrent client
            Exec="{exe}" %u
            Icon=torrent7
            Terminal=false
            Categories=Network;FileTransfer;P2P;
            MimeType=application/x-bittorrent;x-scheme-handler/magnet;
            StartupWMClass=Torrent7
            """;
        var file = Path.Combine(apps, "torrent7.desktop");
        File.WriteAllText(file, desktop.Replace("\r\n", "\n") + "\n");

        var tail = "";
        if (!TryRun("xdg-mime", "default torrent7.desktop x-scheme-handler/magnet application/x-bittorrent"))
            tail = " xdg-mime was not found, so pick Torrent7 for magnet links in your desktop's default apps.";
        TryRun("update-desktop-database", $"\"{apps}\"");
        Log.Info("wrote " + file);
        return "Done. Torrent7 is in the applications menu." + tail;
    }

    private static bool TryRun(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
}
