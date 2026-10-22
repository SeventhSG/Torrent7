using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Torrent7.Core;

namespace Torrent7.ViewModels;

public sealed record SpeedTestRow(string When, string Down, string Up, string Latency);

/// <summary>
/// The built in speed test, what to do with its result (an upload cap that keeps downloads
/// fast, and connection counts sized to the line), and the last few results.
/// </summary>
public sealed partial class SpeedTestViewModel(TorrentService service, HttpClient http, IShell shell, Func<Task> settingsChanged) : ObservableObject
{
    private CancellationTokenSource? cts;
    private SpeedTestResult? result;

    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private string phaseText = "";
    [ObservableProperty] private double phaseProgress;
    // A plain dash until there is a measurement, so the units never stand alone.
    [ObservableProperty] private string downValue = "-";
    [ObservableProperty] private string upValue = "-";
    [ObservableProperty] private string pingValue = "-";
    [ObservableProperty] private string jitterText = "";
    [ObservableProperty] private bool hasResult;
    [ObservableProperty] private bool hasSuggestion;
    [ObservableProperty] private string suggestUpload = "";
    [ObservableProperty] private string suggestConnections = "";
    [ObservableProperty] private string? errorText;
    [ObservableProperty] private int historyVersion;
    [ObservableProperty] private bool downActive;
    [ObservableProperty] private bool upActive;

    public SpeedHistory Live { get; private set; } = new(80);

    // The readings arrive five times a second; the numbers on screen ease toward them every
    // frame, so they count up and settle instead of jumping.
    private readonly Avalonia.Threading.DispatcherTimer glide = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private double downTarget, upTarget, downShown, upShown;
    private bool downLive, upLive;

    private bool glideHooked;

    private void StartGlide()
    {
        if (!glideHooked)
        {
            glideHooked = true;
            glide.Tick += (_, _) =>
            {
                // Nothing live: stop, so an idle page costs nothing.
                if (!downLive && !upLive) { glide.Stop(); return; }
                downShown += (downTarget - downShown) * 0.12;
                upShown += (upTarget - upShown) * 0.12;
                if (downLive) DownValue = Mbps((long)downShown);
                if (upLive) UpValue = Mbps((long)upShown);
            };
        }
        glide.Start();
    }
    public ObservableCollection<SpeedTestRow> History { get; } = [];
    public bool HasHistory => History.Count > 0;
    public bool ShowGraphHint => !IsRunning && Live.Count == 0;
    public string RunLabel => IsRunning ? "Stop" : HasResult ? "Run again" : "Start test";

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(RunLabel));
        OnPropertyChanged(nameof(ShowGraphHint));
    }
    partial void OnHasResultChanged(bool value) => OnPropertyChanged(nameof(RunLabel));

    /// <summary>When the page opens: the last result if there is one, and the history.</summary>
    public void ShowPrevious()
    {
        LoadHistory();
        if (IsRunning || HasResult) return;
        var last = service.Settings.SpeedTests.FirstOrDefault();
        if (last == null)
        {
            PhaseText = "Measures your line against Cloudflare's servers. Torrents are held to a trickle while it runs.";
            return;
        }
        Show(new SpeedTestResult(last.LatencyMs, 0, last.DownBps, last.UpBps));
        JitterText = "";
        PhaseText = $"Last measured {last.WhenUtc.ToLocalTime():yyyy-MM-dd HH:mm}.";
    }

    private void LoadHistory()
    {
        History.Clear();
        foreach (var t in service.Settings.SpeedTests.Take(8))
            History.Add(new SpeedTestRow(t.WhenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                Format.Megabits(t.DownBps), Format.Megabits(t.UpBps), t.LatencyMs > 0 ? $"{t.LatencyMs:0} ms" : ""));
        OnPropertyChanged(nameof(HasHistory));
    }

    private static string Mbps(long bps) => (bps / 1_000_000.0).ToString(bps >= 100_000_000 ? "0" : "0.0", System.Globalization.CultureInfo.InvariantCulture);

    private void Show(SpeedTestResult r)
    {
        result = r;
        DownValue = Mbps(r.DownBps);
        UpValue = Mbps(r.UpBps);
        PingValue = r.LatencyMs > 0 ? r.LatencyMs.ToString("0") : "-";
        HasResult = true;
        HasSuggestion = r.UpBps > 0;
        SuggestUpload = Format.Limit(r.SuggestedUploadLimitKiB);
        SuggestConnections = $"{r.SuggestedMaxConnections}, {r.SuggestedHalfOpen} at once";
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsRunning) { cts?.Cancel(); return; }

        IsRunning = true;
        ErrorText = null;
        DownValue = UpValue = PingValue = "-";
        JitterText = "";
        Live = new SpeedHistory(80); // download fills the left half, upload the right
        OnPropertyChanged(nameof(Live));
        cts = new CancellationTokenSource();
        downTarget = upTarget = downShown = upShown = 0;
        downLive = upLive = false;
        StartGlide();

        var progress = new Progress<SpeedTestProgress>(p =>
        {
            PhaseProgress = p.Fraction * 100;
            DownActive = p.Phase == SpeedTestPhase.Download;
            UpActive = p.Phase == SpeedTestPhase.Upload;
            switch (p.Phase)
            {
                case SpeedTestPhase.Latency:
                    PhaseText = "Measuring latency";
                    if (p.LatencyMs is { } ms) PingValue = ms.ToString("0");
                    break;
                case SpeedTestPhase.Download:
                    PhaseText = "Measuring download";
                    downLive = true;
                    downTarget = p.CurrentBps;
                    Live.Add(p.CurrentBps / 8, 0);
                    HistoryVersion++;
                    break;
                case SpeedTestPhase.Upload:
                    PhaseText = "Measuring upload";
                    downLive = false;
                    upLive = true;
                    upTarget = p.CurrentBps;
                    Live.Add(0, p.CurrentBps / 8);
                    HistoryVersion++;
                    break;
            }
        });

        try
        {
            await service.SuspendTrafficAsync(true);
            var r = await new SpeedTest(http).RunAsync(progress, cts.Token);
            downLive = upLive = false;
            Show(r);
            JitterText = $"{r.JitterMs:0.0} ms jitter";
            PhaseText = $"Measured {DateTime.Now:HH:mm}.";

            var s = service.Settings.Clone();
            s.MeasuredDownBps = r.DownBps;
            s.MeasuredUpBps = r.UpBps;
            s.SpeedTests.Insert(0, new SpeedTestEntry { WhenUtc = DateTime.UtcNow, DownBps = r.DownBps, UpBps = r.UpBps, LatencyMs = r.LatencyMs });
            if (s.SpeedTests.Count > 20) s.SpeedTests.RemoveRange(20, s.SpeedTests.Count - 20);
            await service.ApplySettingsAsync(s);
            LoadHistory();
        }
        catch (OperationCanceledException)
        {
            PhaseText = "Stopped before it finished.";
        }
        catch (Exception ex)
        {
            ErrorText = "Could not reach the speed test servers: " + ex.Message;
            PhaseText = "";
        }
        finally
        {
            await service.SuspendTrafficAsync(false);
            downLive = upLive = false;
            PhaseProgress = 0;
            DownActive = UpActive = false;
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task ApplySuggestionsAsync()
    {
        if (result == null) return;
        var s = service.Settings.Clone();
        s.UploadLimitKiB = result.SuggestedUploadLimitKiB;
        s.MaxConnections = result.SuggestedMaxConnections;
        s.MaxHalfOpen = result.SuggestedHalfOpen;
        await service.ApplySettingsAsync(s);
        await settingsChanged();
        shell.Notify("Tuned to your line", $"Upload capped at {Format.Limit(s.UploadLimitKiB)}, up to {s.MaxConnections} peers.");
    }
}
