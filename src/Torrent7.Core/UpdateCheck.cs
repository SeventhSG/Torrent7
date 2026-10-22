using System.Text.Json;

namespace Torrent7.Core;

/// <summary>
/// Asks GitHub once at start whether a newer release exists. Read only: nothing is downloaded
/// or installed without the user pressing the button, and a failed check is silent.
/// </summary>
public static class UpdateCheck
{
    public const string ReleasesPage = "https://github.com/SeventhSG/Torrent7/releases/latest";

    public static async Task<Version?> NewerAsync(HttpClient http, Version current)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/SeventhSG/Torrent7/releases/latest");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
            return Version.TryParse(tag, out var latest) && latest > current ? latest : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            Log.Warn("update check: " + ex.Message);
            return null;
        }
    }
}
