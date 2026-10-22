// Headless checks for Torrent7.Core. No network and no real profile: the data folder
// is a temp directory that is removed at the end. Covers formatting, torrent input
// recognition, speed schedules, the queue policy, tracker lists, settings, speed test
// suggestions and the speed history.
using Torrent7.Core;

var dataDir = Path.Combine(Path.GetTempPath(), "torrent7-tests-" + Guid.NewGuid().ToString("N"));
AppPaths.UseDataDir(dataDir);

int failures = 0;
void Check(string name, bool passed, string detail)
{
    Console.WriteLine($"{(passed ? "  ok  " : "FAILED")}  {name}");
    if (!passed) { Console.WriteLine($"          {detail}"); failures++; }
}

// Format
foreach (var (bytes, expected) in new (long, string)[]
{
    (0, "0 B"), (1023, "1023 B"), (1024, "1.00 KiB"), (1536, "1.50 KiB"),
    (10L * 1024 * 1024, "10.0 MiB"), (150L * 1024 * 1024 * 1024, "150 GiB"),
})
{
    var actual = Format.Size(bytes);
    Check($"Format.Size({bytes})", actual == expected, $"got \"{actual}\", expected \"{expected}\"");
}

Check("Format.Speed(0)", Format.Speed(0) == "0 B/s", $"got \"{Format.Speed(0)}\", expected \"0 B/s\"");
Check("Format.Limit(0)", Format.Limit(0) == "Unlimited", $"got \"{Format.Limit(0)}\", expected \"Unlimited\"");
Check("Format.Limit(512)", Format.Limit(512) == "512 KiB/s", $"got \"{Format.Limit(512)}\", expected \"512 KiB/s\"");

foreach (var (eta, expected) in new (TimeSpan?, string)[]
{
    (null, ""),
    (TimeSpan.FromSeconds(30), "30 s"),
    (TimeSpan.FromSeconds(90), "1 min 30 s"),
    (TimeSpan.FromHours(2) + TimeSpan.FromMinutes(5), "2 h 5 min"),
    (TimeSpan.FromHours(26), "1 d 2 h"),
    (TimeSpan.FromDays(200), "over 100 d"),
})
{
    var actual = Format.Eta(eta);
    Check($"Format.Eta({eta?.ToString() ?? "null"})", actual == expected, $"got \"{actual}\", expected \"{expected}\"");
}

Check("Format.EtaFor(0, 0)", Format.EtaFor(0, 0) == TimeSpan.Zero, $"got {Format.EtaFor(0, 0)}, expected {TimeSpan.Zero}");
Check("Format.EtaFor(100, 0)", Format.EtaFor(100, 0) == null, $"got {Format.EtaFor(100, 0)}, expected null");
Check("Format.EtaFor(1000, 100)", Format.EtaFor(1000, 100) == TimeSpan.FromSeconds(10), $"got {Format.EtaFor(1000, 100)}, expected 00:00:10");

var clockOk = Format.TryParseClock("07:30", out var clockMinute);
Check("Format.TryParseClock(\"07:30\") succeeds", clockOk, "got false, expected true");
Check("Format.TryParseClock(\"07:30\") minute", clockMinute == 450, $"got {clockMinute}, expected 450");
var clock24 = Format.TryParseClock("24:00", out _);
Check("Format.TryParseClock(\"24:00\") rejected", !clock24, "got true, expected false");
var clock7 = Format.TryParseClock("7", out _);
Check("Format.TryParseClock(\"7\") rejected", !clock7, "got true, expected false");
Check("Format.ClockMinute(450)", Format.ClockMinute(450) == "07:30", $"got \"{Format.ClockMinute(450)}\", expected \"07:30\"");

// TorrentInput
const string magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=x";
var (kindMagnet, _) = TorrentInput.Classify(magnet);
Check("TorrentInput.Classify magnet", kindMagnet == TorrentInputKind.Magnet, $"got {kindMagnet}, expected Magnet");

var (kindEmbedded, valueEmbedded) = TorrentInput.Classify("see " + magnet + " here");
Check("TorrentInput.Classify magnet inside text", kindEmbedded == TorrentInputKind.Magnet, $"got {kindEmbedded}, expected Magnet");
Check("TorrentInput.Classify embedded magnet value", valueEmbedded.StartsWith("magnet:") && !valueEmbedded.EndsWith(" here") && valueEmbedded == magnet,
    $"got \"{valueEmbedded}\", expected \"{magnet}\"");

var (kindHash40, valueHash40) = TorrentInput.Classify("0123456789abcdef0123456789abcdef01234567");
Check("TorrentInput.Classify 40-hex hash", kindHash40 == TorrentInputKind.InfoHash && valueHash40.StartsWith("magnet:?xt=urn:btih:"),
    $"got {kindHash40} \"{valueHash40}\", expected InfoHash starting \"magnet:?xt=urn:btih:\"");

var (kindHash64, valueHash64) = TorrentInput.Classify("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
Check("TorrentInput.Classify 64-hex hash", kindHash64 == TorrentInputKind.InfoHash && valueHash64.StartsWith("magnet:?xt=urn:btmh:1220"),
    $"got {kindHash64} \"{valueHash64}\", expected InfoHash starting \"magnet:?xt=urn:btmh:1220\"");

var kindUrl = TorrentInput.Classify("https://example.org/a.torrent").Kind;
Check("TorrentInput.Classify https url", kindUrl == TorrentInputKind.Url, $"got {kindUrl}, expected Url");

var kindFile = TorrentInput.Classify("C:\\x\\a.torrent").Kind;
Check("TorrentInput.Classify .torrent path", kindFile == TorrentInputKind.File, $"got {kindFile}, expected File");

var kindText = TorrentInput.Classify("hello world").Kind;
Check("TorrentInput.Classify plain text", kindText == TorrentInputKind.None, $"got {kindText}, expected None");

var kindEmpty = TorrentInput.Classify("").Kind;
Check("TorrentInput.Classify empty string", kindEmpty == TorrentInputKind.None, $"got {kindEmpty}, expected None");
var kindNull = TorrentInput.Classify(null).Kind;
Check("TorrentInput.Classify null", kindNull == TorrentInputKind.None, $"got {kindNull}, expected None");

var kindNoHash = TorrentInput.Classify("magnet:?dn=noHash").Kind;
Check("TorrentInput.Classify magnet without xt", kindNoHash != TorrentInputKind.Magnet, $"got {kindNoHash}, expected not Magnet");

Check("TorrentInput.IsWorthOffering magnet", TorrentInput.IsWorthOffering(magnet), "got false, expected true");
Check("TorrentInput.IsWorthOffering bare hash", TorrentInput.IsWorthOffering("0123456789abcdef0123456789abcdef01234567"), "got false, expected true");
var offerPage = TorrentInput.IsWorthOffering("https://example.org/page");
Check("TorrentInput.IsWorthOffering web page", !offerPage, "got true, expected false");

// SpeedSchedule
Check("SpeedSchedule.InWindow 09:00-23:00 at 10:00", SpeedSchedule.InWindow(540, 1380, 600), "got false, expected true");
Check("SpeedSchedule.InWindow 09:00-23:00 at 23:00", !SpeedSchedule.InWindow(540, 1380, 1380), "got true, expected false");
Check("SpeedSchedule.InWindow wrapping at 01:00", SpeedSchedule.InWindow(1320, 420, 60), "got false, expected true");
Check("SpeedSchedule.InWindow wrapping at 10:00", !SpeedSchedule.InWindow(1320, 420, 600), "got true, expected false");
Check("SpeedSchedule.InWindow equal bounds", !SpeedSchedule.InWindow(600, 600, 600), "got true, expected false");

var plain = new AppSettings { DownloadLimitKiB = 100, UploadLimitKiB = 50, SlowMode = false, ScheduleEnabled = false };
var plainLimits = SpeedSchedule.EffectiveLimits(plain, new DateTime(2026, 1, 1, 12, 0, 0));
Check("SpeedSchedule.EffectiveLimits normal", plainLimits == (102400, 51200), $"got {plainLimits}, expected (102400, 51200)");

var slow = new AppSettings { DownloadLimitKiB = 100, UploadLimitKiB = 50, SlowMode = true, SlowDownloadLimitKiB = 10, SlowUploadLimitKiB = 5 };
var slowLimits = SpeedSchedule.EffectiveLimits(slow, new DateTime(2026, 1, 1, 12, 0, 0));
Check("SpeedSchedule.EffectiveLimits slow mode", slowLimits == (10240, 5120), $"got {slowLimits}, expected (10240, 5120)");

var timed = new AppSettings
{
    DownloadLimitKiB = 100, UploadLimitKiB = 50, SlowDownloadLimitKiB = 10, SlowUploadLimitKiB = 5,
    ScheduleEnabled = true, ScheduleFromMinute = 9 * 60, ScheduleToMinute = 17 * 60,
};
var inWindow = SpeedSchedule.EffectiveLimits(timed, new DateTime(2026, 1, 1, 12, 0, 0));
Check("SpeedSchedule.EffectiveLimits schedule inside window", inWindow == (10240, 5120), $"got {inWindow}, expected (10240, 5120)");
var outWindow = SpeedSchedule.EffectiveLimits(timed, new DateTime(2026, 1, 1, 18, 0, 0));
Check("SpeedSchedule.EffectiveLimits schedule outside window", outWindow == (102400, 51200), $"got {outWindow}, expected (102400, 51200)");

// QueuePolicy
static QueueEntry Q(string id, int minute, bool wants = true, bool complete = false, bool running = false, bool forced = false) =>
    new(id, wants, complete, running, new DateTime(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc), forced);

var threeWaiting = QueuePolicy.Decide(new[] { Q("c", 3), Q("a", 1), Q("b", 2) }, 2);
Check("QueuePolicy three waiting, max 2", threeWaiting.Start.SequenceEqual(new[] { "a", "b" }) && threeWaiting.Hold.Count == 0,
    $"got start [{string.Join(",", threeWaiting.Start)}] hold [{string.Join(",", threeWaiting.Hold)}], expected start [a,b] hold []");

var twoRunning = QueuePolicy.Decide(new[] { Q("a", 1, running: true), Q("b", 2, running: true), Q("c", 3) }, 2);
Check("QueuePolicy two running and one waiting, max 2", twoRunning.Start.Count == 0 && twoRunning.Hold.Count == 0,
    $"got start [{string.Join(",", twoRunning.Start)}] hold [{string.Join(",", twoRunning.Hold)}], expected both empty");

var threeRunning = QueuePolicy.Decide(new[] { Q("a", 1, running: true), Q("b", 2, running: true), Q("c", 3, running: true) }, 2);
Check("QueuePolicy three running, max 2 holds newest", threeRunning.Hold.SequenceEqual(new[] { "c" }) && threeRunning.Start.Count == 0,
    $"got start [{string.Join(",", threeRunning.Start)}] hold [{string.Join(",", threeRunning.Hold)}], expected start [] hold [c]");

var seeding = QueuePolicy.Decide(new[] { Q("s", 1, complete: true), Q("a", 2, running: true), Q("b", 3, running: true) }, 1);
Check("QueuePolicy seeding ignores limit", seeding.Start.Contains("s"), $"got start [{string.Join(",", seeding.Start)}], expected it to contain s");

var forcedRun = QueuePolicy.Decide(new[] { Q("f", 1, forced: true), Q("a", 2, running: true), Q("b", 3, running: true) }, 2);
Check("QueuePolicy forced ignores limit", forcedRun.Start.Contains("f"), $"got start [{string.Join(",", forcedRun.Start)}], expected it to contain f");

var unwanted = QueuePolicy.Decide(new[] { Q("w", 1, wants: false), Q("c", 2, complete: true, wants: false), Q("f", 3, forced: true, wants: false) }, 5);
Check("QueuePolicy WantsToRun false never started", unwanted.Start.Count == 0 && unwanted.Hold.Count == 0,
    $"got start [{string.Join(",", unwanted.Start)}] hold [{string.Join(",", unwanted.Hold)}], expected both empty");

// TrackerBoost
var parsed = TrackerBoost.Parse("udp://a.example:1337/announce\n\n# comment\nhttp://b.example/announce\nnot a url\nudp://a.example:1337/announce");
Check("TrackerBoost.Parse keeps 2 trackers", parsed.Count == 2, $"got {parsed.Count} [{string.Join(", ", parsed)}], expected 2");

var missing = TrackerBoost.Missing(
    new[] { "udp://a.example:1337/announce/" },
    new[] { "udp://a.example:1337/announce", "udp://c.example:80/announce" }).ToList();
Check("TrackerBoost.Missing returns only new tracker", missing.SequenceEqual(new[] { "udp://c.example:80/announce" }),
    $"got [{string.Join(", ", missing)}], expected [udp://c.example:80/announce]");

// AppSettings
var lowPort = new AppSettings { ListenPort = 5 }.Normalised();
Check("AppSettings.Normalised ListenPort 5 in range", lowPort.ListenPort is >= 1024 and <= 65535, $"got {lowPort.ListenPort}, expected 1024..65535");
var lowConns = new AppSettings { MaxConnections = 1 }.Normalised();
Check("AppSettings.Normalised MaxConnections 1 becomes 20", lowConns.MaxConnections == 20, $"got {lowConns.MaxConnections}, expected 20");
var negLimit = new AppSettings { DownloadLimitKiB = -5 }.Normalised();
Check("AppSettings.Normalised negative limit becomes 0", negLimit.DownloadLimitKiB == 0, $"got {negLimit.DownloadLimitKiB}, expected 0");
var zeroActive = new AppSettings { MaxActiveDownloads = 0 }.Normalised();
Check("AppSettings.Normalised MaxActiveDownloads 0 becomes 1", zeroActive.MaxActiveDownloads == 1, $"got {zeroActive.MaxActiveDownloads}, expected 1");

var saved = new AppSettings
{
    DownloadLimitKiB = 4321, UploadLimitKiB = 321, MaxActiveDownloads = 3, SeedRatioLimit = 1.5,
    ScheduleEnabled = true, ScheduleFromMinute = 600, Encryption = EncryptionMode.Require,
    WatchClipboard = false, DownloadDir = Path.Combine(dataDir, "downloads"),
};
saved.Save();
var loaded = AppSettings.Load();
var roundTrip = loaded.DownloadLimitKiB == saved.DownloadLimitKiB
    && loaded.UploadLimitKiB == saved.UploadLimitKiB
    && loaded.MaxActiveDownloads == saved.MaxActiveDownloads
    && loaded.SeedRatioLimit == saved.SeedRatioLimit
    && loaded.ScheduleEnabled == saved.ScheduleEnabled
    && loaded.ScheduleFromMinute == saved.ScheduleFromMinute
    && loaded.Encryption == saved.Encryption
    && loaded.WatchClipboard == saved.WatchClipboard
    && loaded.DownloadDir == saved.DownloadDir;
Check("AppSettings Save and Load round trip", roundTrip,
    $"got down {loaded.DownloadLimitKiB}, up {loaded.UploadLimitKiB}, active {loaded.MaxActiveDownloads}, ratio {loaded.SeedRatioLimit}, " +
    $"schedule {loaded.ScheduleEnabled}/{loaded.ScheduleFromMinute}, enc {loaded.Encryption}, clipboard {loaded.WatchClipboard}, dir {loaded.DownloadDir}; " +
    $"expected down {saved.DownloadLimitKiB}, up {saved.UploadLimitKiB}, active {saved.MaxActiveDownloads}, ratio {saved.SeedRatioLimit}, " +
    $"schedule {saved.ScheduleEnabled}/{saved.ScheduleFromMinute}, enc {saved.Encryption}, clipboard {saved.WatchClipboard}, dir {saved.DownloadDir}");

// SpeedTestResult
var fast = new SpeedTestResult(20, 2, 100_000_000, 20_000_000);
var expectedUpload = (int)(20_000_000 * 0.8 / 8 / 1024);
Check("SpeedTestResult.SuggestedUploadLimitKiB at 20 Mbps up", fast.SuggestedUploadLimitKiB == expectedUpload,
    $"got {fast.SuggestedUploadLimitKiB}, expected {expectedUpload}");
Check("SpeedTestResult.SuggestedMaxConnections at 100 Mbps down", fast.SuggestedMaxConnections == 400,
    $"got {fast.SuggestedMaxConnections}, expected 400");
var slowLine = new SpeedTestResult(20, 2, 5_000_000, 1_000_000);
Check("SpeedTestResult.SuggestedMaxConnections at 5 Mbps down", slowLine.SuggestedMaxConnections == 150,
    $"got {slowLine.SuggestedMaxConnections}, expected 150");
var noUp = new SpeedTestResult(20, 2, 100_000_000, 0);
Check("SpeedTestResult.SuggestedUploadLimitKiB with no uplink", noUp.SuggestedUploadLimitKiB == 0,
    $"got {noUp.SuggestedUploadLimitKiB}, expected 0");

// SpeedHistory
var history = new SpeedHistory(3);
history.Add(1, 0);
history.Add(2, 0);
history.Add(3, 0);
history.Add(4, 0);
Check("SpeedHistory keeps 3 samples", history.Count == 3, $"got {history.Count}, expected 3");
var downs = history.Samples().Select(s => s.Down).ToArray();
Check("SpeedHistory samples oldest first", downs.SequenceEqual(new long[] { 2, 3, 4 }),
    $"got [{string.Join(", ", downs)}], expected [2, 3, 4]");
Check("SpeedHistory peak", history.Peak == 4, $"got {history.Peak}, expected 4");

// A real download over loopback, through the same service the app uses.
try
{
    await Torrent7.Tests.LoopbackDownload.RunAsync(Check, Path.Combine(dataDir, "loopback"));
}
catch (Exception ex)
{
    Check("loopback download ran", false, ex.ToString());
}

// Cleanup
try { Directory.Delete(dataDir, true); } catch (Exception) { }

Console.WriteLine(failures == 0 ? "\nAll checks passed." : $"\n{failures} check(s) failed.");
return failures == 0 ? 0 : 1;
