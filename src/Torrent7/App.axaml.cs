using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Torrent7.Core;
using Torrent7.ViewModels;
using Torrent7.Views;

namespace Torrent7;

public sealed class App : Application
{
    private TorrentService? service;
    private MainWindow? window;
    private HttpClient? http;
    private bool quitting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        // The window owns the app's life only when it is really closing; hiding to the tray is not quitting.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        MainViewModel.Ui = a => Dispatcher.UIThread.Post(a);

        http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 16,
        })
        { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Torrent7/{typeof(App).Assembly.GetName().Version?.ToString(3)}");

        var settings = AppSettings.Load();
        settings.Save();
        service = new TorrentService(settings, http);

        window = new MainWindow();
        desktop.MainWindow = window;
        window.Show();

        try
        {
            await service.StartAsync();
        }
        catch (Exception ex)
        {
            Log.Error("engine start", ex);
            window.ShowFatal("Torrent7 could not start its engine", ex.Message +
                "\n\nAnother program may be using the same port. Change the port in settings.json and start again.");
            return;
        }

        var vm = new MainViewModel(service, http, window);
        window.Attach(vm, () => service.Settings.CloseToTray && !quitting, Quit);
        _ = TrackerBoost.RefreshAsync(http);

        foreach (var arg in Program.StartupArgs) await vm.AddSourceAsync(arg);
        Program.ArgumentReceived = arg => Dispatcher.UIThread.Post(async () =>
        {
            window.BringToFront();
            if (!string.IsNullOrWhiteSpace(arg)) await vm.AddSourceAsync(arg);
        });

        desktop.ShutdownRequested += async (_, e) =>
        {
            if (quitting) return;
            e.Cancel = true;
            await QuitAsync();
        };

        base.OnFrameworkInitializationCompleted();
    }

    private void Quit() => _ = QuitAsync();

    private async Task QuitAsync()
    {
        if (quitting) return;
        quitting = true;
        window?.ShowClosing();
        if (service != null) await service.DisposeAsync();
        http?.Dispose();
        Log.Info("quit");
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }

    private void OnTrayClicked(object? sender, EventArgs e) => window?.BringToFront();
    private void OnTrayShow(object? sender, EventArgs e) => window?.BringToFront();
    private void OnTrayPauseAll(object? sender, EventArgs e) => _ = service?.PauseAllAsync();
    private void OnTrayResumeAll(object? sender, EventArgs e) => _ = service?.ResumeAllAsync();
    private void OnTrayQuit(object? sender, EventArgs e) => Quit();
}
