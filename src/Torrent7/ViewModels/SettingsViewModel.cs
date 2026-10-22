using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Torrent7.Core;
using Torrent7.Platform;

namespace Torrent7.ViewModels;

/// <summary>
/// Every setting as its own property, compared against what is saved, so the page knows
/// when there is something to save and shows the bar for it only then. Numbers are decimals
/// because that is what a NumericUpDown speaks.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly TorrentService service;
    private readonly IShell shell;
    private readonly Action saved;
    private string savedJson = "";
    private bool loading;

    public SettingsViewModel(TorrentService service, IShell shell, Action saved)
    {
        this.service = service;
        this.shell = shell;
        this.saved = saved;
        Reload();
    }

    public static IReadOnlyList<string> EncryptionNames { get; } =
        ["Prefer encrypted connections", "Only encrypted connections", "Prefer plain (fastest)"];

    [ObservableProperty] private string downloadDir = "";
    [ObservableProperty] private bool showAddDialog;
    [ObservableProperty] private bool startImmediately;
    [ObservableProperty] private decimal? maxActive;
    [ObservableProperty] private string? watchFolder;

    [ObservableProperty] private decimal? downloadLimit;
    [ObservableProperty] private decimal? uploadLimit;
    [ObservableProperty] private decimal? slowDownloadLimit;
    [ObservableProperty] private decimal? slowUploadLimit;
    [ObservableProperty] private bool scheduleEnabled;
    [ObservableProperty] private string scheduleFrom = "";
    [ObservableProperty] private string scheduleTo = "";

    [ObservableProperty] private decimal? port;
    [ObservableProperty] private bool portForwarding;
    [ObservableProperty] private bool dht;
    [ObservableProperty] private bool localPeerDiscovery;
    [ObservableProperty] private int encryptionIndex;
    [ObservableProperty] private decimal? maxConnections;
    [ObservableProperty] private decimal? perTorrent;
    [ObservableProperty] private decimal? halfOpen;

    [ObservableProperty] private decimal? seedRatio;

    [ObservableProperty] private bool trackerBoost;
    [ObservableProperty] private bool watchClipboard;
    [ObservableProperty] private bool notifyOnComplete;
    [ObservableProperty] private bool closeToTray;
    [ObservableProperty] private decimal? diskCache;
    [ObservableProperty] private bool scanDownloads;
    [ObservableProperty] private string? virusTotalKey;

    public string ScannerName => OperatingSystem.IsWindows() ? "Windows Defender" : "ClamAV";

    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private string? error;
    [ObservableProperty] private string integrationStatus = "";

    public string DataDirText => AppPaths.IsPortable ? $"Portable, in {AppPaths.DataDir}" : AppPaths.DataDir;
    public string IntegrationLabel => OperatingSystem.IsWindows() ? "Make Torrent7 the default" : "Add to applications menu";
    public string IntegrationHint => OperatingSystem.IsWindows()
        ? "Magnet links in your browser and .torrent files you double click open in Torrent7."
        : "Adds Torrent7 to the menu and opens magnet links with it.";

    public void Reload()
    {
        loading = true;
        var s = service.Settings;
        DownloadDir = s.DownloadDir;
        ShowAddDialog = s.ShowAddDialog;
        StartImmediately = s.StartImmediately;
        MaxActive = s.MaxActiveDownloads;
        WatchFolder = s.WatchFolder;
        DownloadLimit = s.DownloadLimitKiB;
        UploadLimit = s.UploadLimitKiB;
        SlowDownloadLimit = s.SlowDownloadLimitKiB;
        SlowUploadLimit = s.SlowUploadLimitKiB;
        ScheduleEnabled = s.ScheduleEnabled;
        ScheduleFrom = Format.ClockMinute(s.ScheduleFromMinute);
        ScheduleTo = Format.ClockMinute(s.ScheduleToMinute);
        Port = s.ListenPort;
        PortForwarding = s.PortForwarding;
        Dht = s.Dht;
        LocalPeerDiscovery = s.LocalPeerDiscovery;
        EncryptionIndex = s.Encryption switch { EncryptionMode.Require => 1, EncryptionMode.Allow => 2, _ => 0 };
        MaxConnections = s.MaxConnections;
        PerTorrent = s.MaxConnectionsPerTorrent;
        HalfOpen = s.MaxHalfOpen;
        SeedRatio = (decimal)s.SeedRatioLimit;
        TrackerBoost = s.TrackerBoost;
        WatchClipboard = s.WatchClipboard;
        NotifyOnComplete = s.NotifyOnComplete;
        CloseToTray = s.CloseToTray;
        DiskCache = s.DiskCacheMiB;
        ScanDownloads = s.ScanDownloads;
        VirusTotalKey = s.VirusTotalKey;
        Error = null;
        loading = false;
        savedJson = Snapshot(Build(out _));
        IsDirty = false;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (loading || e.PropertyName is nameof(IsDirty) or nameof(Error) or nameof(IntegrationStatus)) return;
        IsDirty = Snapshot(Build(out _)) != savedJson;
    }

    private static string Snapshot(AppSettings s) => JsonSerializer.Serialize(s, Core.JsonAccess.Settings);

    /// <summary>The settings as they stand on the page. Bad schedule times are reported, not guessed.</summary>
    private AppSettings Build(out string? problem)
    {
        problem = null;
        var s = service.Settings.Clone();
        s.DownloadDir = DownloadDir.Trim();
        s.ShowAddDialog = ShowAddDialog;
        s.StartImmediately = StartImmediately;
        s.MaxActiveDownloads = (int)(MaxActive ?? 5);
        s.WatchFolder = string.IsNullOrWhiteSpace(WatchFolder) ? null : WatchFolder.Trim();
        s.DownloadLimitKiB = (int)(DownloadLimit ?? 0);
        s.UploadLimitKiB = (int)(UploadLimit ?? 0);
        s.SlowDownloadLimitKiB = (int)(SlowDownloadLimit ?? 0);
        s.SlowUploadLimitKiB = (int)(SlowUploadLimit ?? 0);
        s.ScheduleEnabled = ScheduleEnabled;
        if (Format.TryParseClock(ScheduleFrom, out var from)) s.ScheduleFromMinute = from; else problem = "Schedule times need the form 23:30.";
        if (Format.TryParseClock(ScheduleTo, out var to)) s.ScheduleToMinute = to; else problem = "Schedule times need the form 23:30.";
        s.ListenPort = (int)(Port ?? 0);
        s.PortForwarding = PortForwarding;
        s.Dht = Dht;
        s.LocalPeerDiscovery = LocalPeerDiscovery;
        s.Encryption = EncryptionIndex switch { 1 => EncryptionMode.Require, 2 => EncryptionMode.Allow, _ => EncryptionMode.Prefer };
        s.MaxConnections = (int)(MaxConnections ?? 500);
        s.MaxConnectionsPerTorrent = (int)(PerTorrent ?? 150);
        s.MaxHalfOpen = (int)(HalfOpen ?? 64);
        s.SeedRatioLimit = (double)(SeedRatio ?? 0);
        s.TrackerBoost = TrackerBoost;
        s.WatchClipboard = WatchClipboard;
        s.NotifyOnComplete = NotifyOnComplete;
        s.CloseToTray = CloseToTray;
        s.DiskCacheMiB = (int)(DiskCache ?? 64);
        s.ScanDownloads = ScanDownloads;
        s.VirusTotalKey = string.IsNullOrWhiteSpace(VirusTotalKey) ? null : VirusTotalKey.Trim();
        return s;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var s = Build(out var problem);
        if (problem != null)
        {
            Error = problem;
            return;
        }
        try
        {
            await service.ApplySettingsAsync(s);
            Reload();
            saved();
            shell.Notify("Settings saved", "They are already in effect.");
        }
        catch (Exception ex)
        {
            Error = "Could not apply them: " + ex.Message;
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
        try { IntegrationStatus = DesktopIntegration.Register(); }
        catch (Exception ex) { IntegrationStatus = "Could not register: " + ex.Message; }
    }

    [RelayCommand]
    private void UnregisterHandlers()
    {
        try { IntegrationStatus = DesktopIntegration.Unregister(); }
        catch (Exception ex) { IntegrationStatus = "Could not remove: " + ex.Message; }
    }

    [RelayCommand]
    private Task OpenDataFolderAsync() => shell.OpenAsync(AppPaths.DataDir);
}
