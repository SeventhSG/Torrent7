using System.IO.Pipes;
using System.Text;
using Avalonia;
using Torrent7.Core;

namespace Torrent7;

internal static class Program
{
    // One pipe per user, so two people on one machine each get their own Torrent7.
    private static readonly string PipeName = "torrent7-" + Environment.UserName.ToLowerInvariant();

    public static string[] StartupArgs { get; private set; } = [];

    /// <summary>A second launch's arguments, handed over by the first instance's pipe.</summary>
    public static Action<string>? ArgumentReceived { get; set; }

    [STAThread]
    public static int Main(string[] args)
    {
        StartupArgs = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

        // Clicking a magnet link starts a second copy. Hand the link to the one already running.
        if (TrySendToRunning(StartupArgs)) return 0;

        _ = Task.Run(ListenAsync);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("unhandled", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("unobserved task", e.Exception.InnerException ?? e.Exception);
            e.SetObserved();
        };

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions { WmClass = "Torrent7" })
            .LogToTrace();

    private static bool TrySendToRunning(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(300);
            using var writer = new StreamWriter(client, Encoding.UTF8);
            // An empty line still means "come to the front".
            if (args.Length == 0) writer.WriteLine();
            foreach (var a in args) writer.WriteLine(a);
            writer.Flush();
            return true;
        }
        catch (TimeoutException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static async Task ListenAsync()
    {
        while (true)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync();
                using var reader = new StreamReader(server, Encoding.UTF8);
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                    ArgumentReceived?.Invoke(line.Trim());
            }
            catch (IOException ex)
            {
                Log.Warn("instance pipe: " + ex.Message);
                await Task.Delay(1000);
            }
        }
    }
}
