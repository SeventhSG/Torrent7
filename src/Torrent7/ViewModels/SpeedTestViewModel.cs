using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Torrent7.Core;

namespace Torrent7.ViewModels;

/// <summary>
/// The built in speed test, and what to do with its result: an upload cap that keeps
/// downloads fast, and connection counts sized to the line.
/// </summary>
public sealed partial class SpeedTestViewModel(TorrentService service, HttpClient http, IShell shell, Func<Task> settingsChanged) : ObservableObject
{
    private CancellationTokenSource? cts;

    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private string phaseText = "Measures your line against Cloudflare's speed test servers. Torrents are held to a trickle while it runs, so they do not skew the result.";
    [ObservableProperty] private double phaseProgress;
    [ObservableProperty] private string liveText = "";
    [ObservableProperty] private string downText = "";
    [ObservableProperty] private string upText = "";
    [ObservableProperty] private string pingText = "";
    [ObservableProperty] private string jitterText = "";
    [ObservableProperty] private bool hasResult;
    [ObservableProperty] private string suggestionText = "";
    [ObservableProperty] private string? errorText;
    [ObservableProperty] private int historyVersion;
    [ObservableProperty] private bool showChart;

    public SpeedHistory Live { get; private set; } = new(80);
    private SpeedTestResult? result;

    public void ShowPrevious()
    {
        var s = service.Settings;
        if (s.MeasuredDownBps <= 0 || HasResult) return;
        DownText = Format.Megabits(s.MeasuredDownBps);
        UpText = Format.Megabits(s.MeasuredUpBps);
        PingText = "";
        JitterText = "Run the test again to measure";
        PhaseText = "Your last result. Run the test again for a fresh one.";
        HasResult = true;
        result = new SpeedTestResult(0, 0, s.MeasuredDownBps, s.MeasuredUpBps);
        SuggestionText = Suggest(result);
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsRunning) { cts?.Cancel(); return; }

        IsRunning = true;
        ErrorText = null;
        HasResult = false;
        DownText = UpText = PingText = JitterText = LiveText = "";
        // Room for both phases: download fills the left half, upload the right.
        Live = new SpeedHistory(80);
        OnPropertyChanged(nameof(Live));
        ShowChart = true;
        cts = new CancellationTokenSource();

        var progress = new Progress<SpeedTestProgress>(p =>
        {
            PhaseProgress = p.Fraction * 100;
            switch (p.Phase)
            {
                case SpeedTestPhase.Latency:
                    PhaseText = "Measuring latency";
                    if (p.LatencyMs is { } ms) PingText = $"{ms:0} ms";
                    break;
                case SpeedTestPhase.Download:
                case SpeedTestPhase.Upload:
                    var down = p.Phase == SpeedTestPhase.Download;
                    PhaseText = down ? "Measuring download" : "Measuring upload";
                    LiveText = Format.Megabits(p.CurrentBps);
                    if (down) DownText = LiveText; else UpText = LiveText;
                    if (down) Live.Add(p.CurrentBps / 8, 0); else Live.Add(0, p.CurrentBps / 8);
                    HistoryVersion++;
                    break;
            }
        });

        try
        {
            await service.SuspendTrafficAsync(true);
            var r = await new SpeedTest(http).RunAsync(progress, cts.Token);
            result = r;
            DownText = Format.Megabits(r.DownBps);
            UpText = Format.Megabits(r.UpBps);
            PingText = $"{r.LatencyMs:0} ms";
            JitterText = $"{r.JitterMs:0.0} ms jitter";
            SuggestionText = Suggest(r);
            HasResult = true;
            PhaseText = "Done.";

            var s = service.Settings.Clone();
            s.MeasuredDownBps = r.DownBps;
            s.MeasuredUpBps = r.UpBps;
            await service.ApplySettingsAsync(s);
        }
        catch (OperationCanceledException)
        {
            PhaseText = "Stopped.";
        }
        catch (Exception ex)
        {
            ErrorText = "The test could not reach the speed test servers: " + ex.Message;
            PhaseText = "";
        }
        finally
        {
            await service.SuspendTrafficAsync(false);
            LiveText = "";
            PhaseProgress = 0;
            IsRunning = false;
        }
    }

    private static string Suggest(SpeedTestResult r) =>
        r.UpBps <= 0 ? "" :
        $"Cap upload at {Format.Limit(r.SuggestedUploadLimitKiB)} (80% of your uplink) so downloads keep their speed, " +
        $"and allow {r.SuggestedMaxConnections} connections, {r.SuggestedHalfOpen} opening at once.";

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
        shell.Notify("Tuned to your line", $"Upload capped at {Format.Limit(s.UploadLimitKiB)}, {s.MaxConnections} connections.");
    }
}
