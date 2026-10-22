using System.Diagnostics;

namespace Torrent7.Core;

public enum SpeedTestPhase { Latency, Download, Upload, Done }

public readonly record struct SpeedTestProgress(SpeedTestPhase Phase, double Fraction, long CurrentBps, double? LatencyMs);

public sealed record SpeedTestResult(double LatencyMs, double JitterMs, long DownBps, long UpBps)
{
    /// <summary>
    /// The upload cap that keeps downloads fast. A saturated uplink queues the small
    /// acknowledgements every download depends on, and the download stalls with it. Capping
    /// at about 80% of the measured uplink leaves them room.
    /// </summary>
    public int SuggestedUploadLimitKiB => UpBps <= 0 ? 0 : (int)Math.Max(16, UpBps * 0.8 / 8 / 1024);

    /// <summary>Connections scaled to the line, because a slow line cannot feed hundreds of peers usefully.</summary>
    public int SuggestedMaxConnections => (DownBps / 1_000_000) switch
    {
        < 10 => 150,
        < 50 => 250,
        < 200 => 400,
        < 600 => 600,
        _ => 900,
    };

    public int SuggestedHalfOpen => Math.Clamp(SuggestedMaxConnections / 8, 24, 120);
}

/// <summary>
/// Measures the line against Cloudflare's public speed test endpoints: latency first, then
/// several parallel streams each way for a fixed time, the way browser speed tests do it.
/// Several streams matter because a single TCP stream rarely fills a fast line on its own.
/// </summary>
public sealed class SpeedTest(HttpClient http)
{
    private const string Base = "https://speed.cloudflare.com";
    private static readonly TimeSpan PhaseLength = TimeSpan.FromSeconds(8);
    private const int Streams = 6;

    public async Task<SpeedTestResult> RunAsync(IProgress<SpeedTestProgress>? progress, CancellationToken token)
    {
        var (latency, jitter) = await MeasureLatencyAsync(progress, token);
        var down = await MeasureAsync(SpeedTestPhase.Download, DownloadLoopAsync, latency, progress, token);
        var up = await MeasureAsync(SpeedTestPhase.Upload, UploadLoopAsync, latency, progress, token);
        progress?.Report(new SpeedTestProgress(SpeedTestPhase.Done, 1, 0, latency));
        return new SpeedTestResult(latency, jitter, down, up);
    }

    private async Task<(double Median, double Jitter)> MeasureLatencyAsync(IProgress<SpeedTestProgress>? progress, CancellationToken token)
    {
        var samples = new List<double>();
        const int count = 10;
        for (var i = 0; i < count; i++)
        {
            var sw = Stopwatch.StartNew();
            using var resp = await http.GetAsync($"{Base}/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, token);
            sw.Stop();
            resp.EnsureSuccessStatusCode();
            // The first request pays for DNS and the TLS handshake, which is not latency.
            if (i > 0) samples.Add(sw.Elapsed.TotalMilliseconds);
            progress?.Report(new SpeedTestProgress(SpeedTestPhase.Latency, (i + 1) / (double)count, 0, samples.Count > 0 ? Median(samples) : null));
        }
        var median = Median(samples);
        var jitter = samples.Zip(samples.Skip(1), (a, b) => Math.Abs(a - b)).DefaultIfEmpty(0).Average();
        return (median, jitter);
    }

    private delegate Task LoopBody(Action<long> count, CancellationToken token);

    private async Task<long> MeasureAsync(SpeedTestPhase phase, LoopBody body, double latency, IProgress<SpeedTestProgress>? progress, CancellationToken outer)
    {
        long total = 0;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(outer);
        void Count(long n) => Interlocked.Add(ref total, n);

        var clock = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, Streams).Select(_ => Task.Run(() => body(Count, stop.Token), stop.Token)).ToArray();

        // The first second is TCP slow start, not the line, so it is left out of the figure.
        var warmup = TimeSpan.FromSeconds(1);
        long atWarmup = 0;
        var warmed = false;
        long lastBytes = 0;
        var lastTick = TimeSpan.Zero;
        double smoothed = 0;

        while (clock.Elapsed < PhaseLength)
        {
            await Task.Delay(200, outer);
            var now = clock.Elapsed;
            var bytes = Interlocked.Read(ref total);
            if (!warmed && now >= warmup) { atWarmup = bytes; warmed = true; }

            var instant = (bytes - lastBytes) * 8 / Math.Max(0.001, (now - lastTick).TotalSeconds);
            smoothed = smoothed == 0 ? instant : smoothed * 0.7 + instant * 0.3;
            lastBytes = bytes;
            lastTick = now;
            progress?.Report(new SpeedTestProgress(phase, Math.Min(1, now / PhaseLength), (long)smoothed, latency));
        }

        var measured = clock.Elapsed - warmup;
        var bits = (Interlocked.Read(ref total) - atWarmup) * 8;
        stop.Cancel();
        try { await Task.WhenAll(workers); } catch (OperationCanceledException) { } catch (HttpRequestException) { }
        outer.ThrowIfCancellationRequested();
        return (long)(bits / Math.Max(0.5, measured.TotalSeconds));
    }

    private async Task DownloadLoopAsync(Action<long> count, CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        while (!token.IsCancellationRequested)
        {
            using var resp = await http.GetAsync($"{Base}/__down?bytes=50000000", HttpCompletionOption.ResponseHeadersRead, token);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(token);
            int n;
            while ((n = await stream.ReadAsync(buffer, token)) > 0)
                count(n);
        }
    }

    private async Task UploadLoopAsync(Action<long> count, CancellationToken token)
    {
        var chunk = new byte[4 * 1024 * 1024];
        Random.Shared.NextBytes(chunk); // incompressible, so nothing on the path can shrink it
        while (!token.IsCancellationRequested)
        {
            using var content = new CountingContent(chunk, count);
            using var resp = await http.PostAsync($"{Base}/__up", content, token);
        }
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>Reports bytes as they leave, so the upload figure moves while a request is in flight.</summary>
    private sealed class CountingContent(byte[] data, Action<long> count) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            const int slice = 64 * 1024;
            for (var offset = 0; offset < data.Length; offset += slice)
            {
                var n = Math.Min(slice, data.Length - offset);
                await stream.WriteAsync(data.AsMemory(offset, n));
                count(n);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = data.Length;
            return true;
        }
    }
}
