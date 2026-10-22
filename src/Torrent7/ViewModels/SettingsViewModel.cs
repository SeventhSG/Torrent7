using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Torrent7.Core;
using Torrent7.Platform;

namespace Torrent7.ViewModels;

/// <summary>
/// Edits a copy of the settings; nothing reaches the engine until Save. Numbers are exposed
/// as decimals because that is what a NumericUpDown speaks.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly TorrentService service;
    private readonly IShell shell;
    private readonly Action saved;

    public SettingsViewModel(TorrentService service, IShell shell, Action saved)
    {
        this.service = service;
        this.shell = shell;
        this.saved = saved;
        Reload();
    }

    [ObservableProperty] private AppSettings edit = new();
    [ObservableProperty] private string downloadDir = "";
    [ObservableProperty] private string? watchFolder;
    [ObservableProperty] private decimal? downloadLimit;
    [ObservableProperty] private decimal? uploadLimit;
    [ObservableProperty] private decimal? slowDownloadLimit;
    [ObservableProperty] private decimal? slowUploadLimit;
    [ObservableProperty] private string scheduleFrom = "";
    [ObservableProperty] private string scheduleTo = "";
    [ObservableProperty] private decimal? maxActive;
    [ObservableProperty] private decimal? maxConnections;
    [ObservableProperty] private decimal? perTorrent;
    [ObservableProperty] private decimal? halfOpen;
    [ObservableProperty] private decimal? port;
    [ObservableProperty] private decimal? diskCache;
    [ObservableProperty] private decimal? seedRatio;
    [ObservableProperty] private int encryptionIndex;
    [ObservableProperty] private string? error;
    [ObservableProperty] private string integrationStatus = "";

    public static IReadOnlyList<string> EncryptionNames { get; } =
        ["Prefer encrypted", "Encrypted only", "Prefer plain (fastest)"];

    public string DataDirText => AppPaths.IsPortable ? $"Portable mode: {AppPaths.DataDir}" : AppPaths.DataDir;
    public string IntegrationLabel => OperatingSystem.IsWindows()
        ? "Open magnet links and .torrent files with Torrent7"
        : "Add Torrent7 to the applications menu and open magnet links with it";

    public void Reload()
    {
        var s = service.Settings.Clone();
        Edit = s;
        DownloadDir = s.DownloadDir;
        WatchFolder = s.WatchFolder;
        DownloadLimit = s.DownloadLimitKiB;
        UploadLimit = s.UploadLimitKiB;
        SlowDownloadLimit = s.SlowDownloadLimitKiB;
        SlowUploadLimit = s.SlowUploadLimitKiB;
        ScheduleFrom = Format.ClockMinute(s.ScheduleFromMinute);
        ScheduleTo = Format.ClockMinute(s.ScheduleToMinute);
        MaxActive = s.MaxActiveDownloads;
        MaxConnections = s.MaxConnections;
        PerTorrent = s.MaxConnectionsPerTorrent;
        HalfOpen = s.MaxHalfOpen;
        Port = s.ListenPort;
        DiskCache = s.DiskCacheMiB;
        SeedRatio = (decimal)s.SeedRatioLimit;
        EncryptionIndex = s.Encryption switch { EncryptionMode.Require => 1, EncryptionMode.Allow => 2, _ => 0 };
        Error = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!Format.TryParseClock(ScheduleFrom, out var from) || !Format.TryParseClock(ScheduleTo, out var to))
        {
            Error = "Schedule times need the form 23:30.";
            return;
        }

        var s = Edit.Clone();
        s.DownloadDir = DownloadDir.Trim();
        s.WatchFolder = string.IsNullOrWhiteSpace(WatchFolder) ? null : WatchFolder.Trim();
        s.DownloadLimitKiB = (int)(DownloadLimit ?? 0);
        s.UploadLimitKiB = (int)(UploadLimit ?? 0);
        s.SlowDownloadLimitKiB = (int)(SlowDownloadLimit ?? 0);
        s.SlowUploadLimitKiB = (int)(SlowUploadLimit ?? 0);
        s.ScheduleFromMinute = from;
        s.ScheduleToMinute = to;
        s.MaxActiveDownloads = (int)(MaxActive ?? 5);
        s.MaxConnections = (int)(MaxConnections ?? 500);
        s.MaxConnectionsPerTorrent = (int)(PerTorrent ?? 150);
        s.MaxHalfOpen = (int)(HalfOpen ?? 64);
        s.ListenPort = (int)(Port ?? 0);
        s.DiskCacheMiB = (int)(DiskCache ?? 64);
        s.SeedRatioLimit = (double)(SeedRatio ?? 0);
        s.Encryption = EncryptionIndex switch { 1 => EncryptionMode.Require, 2 => EncryptionMode.Allow, _ => EncryptionMode.Prefer };

        try
        {
            await service.ApplySettingsAsync(s);
            Reload();
            saved();
            shell.Notify("Settings saved", "They apply right away.");
        }
        catch (Exception ex)
        {
            Error = "Could not apply: " + ex.Message;
        }
    }

    [RelayCommand]
    private void Revert() => Reload();

    [RelayCommand]
    private async Task BrowseDownloadDirAsync()
    {
        var picked = await shell.PickFolderAsync(DownloadDir);
        if (picked != null) DownloadDir = picked;
    }

    [RelayCommand]
    private async Task BrowseWatchFolderAsync()
    {
        var picked = await shell.PickFolderAsync(WatchFolder ?? DownloadDir);
        if (picked != null) WatchFolder = picked;
    }

    [RelayCommand]
    private void ClearWatchFolder() => WatchFolder = null;

    [RelayCommand]
    private void NewPort() => Port = Random.Shared.Next(49160, 65000);

    [RelayCommand]
    private void RegisterHandlers()
    {
        try
        {
            IntegrationStatus = DesktopIntegration.Register();
        }
        catch (Exception ex)
        {
            IntegrationStatus = "Could not register: " + ex.Message;
        }
    }

    [RelayCommand]
    private void UnregisterHandlers()
    {
        try
        {
            IntegrationStatus = DesktopIntegration.Unregister();
        }
        catch (Exception ex)
        {
            IntegrationStatus = "Could not remove: " + ex.Message;
        }
    }

    [RelayCommand]
    private Task OpenDataFolderAsync() => shell.OpenAsync(AppPaths.DataDir);
}
