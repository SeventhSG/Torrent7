using Avalonia;

namespace Torrent7.ViewModels;

/// <summary>Reads an app resource (an icon, a brush) by key, for view models that pick visuals.</summary>
internal static class Res
{
    public static T? Get<T>(string key) where T : class =>
        Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var value) ? value as T : null;
}
