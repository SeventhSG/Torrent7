using System.Text.Json.Serialization;

namespace Torrent7.Core;

/// <summary>
/// Serializers generated at build time, so the published app can be trimmed without losing
/// the code that reads settings and the session back.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<TorrentRecord>))]
internal sealed partial class JsonContext : JsonSerializerContext;

/// <summary>The generated serializer for settings, for the app to compare settings without reflection.</summary>
public static class JsonAccess
{
    public static System.Text.Json.Serialization.Metadata.JsonTypeInfo<AppSettings> Settings => JsonContext.Default.AppSettings;
}
