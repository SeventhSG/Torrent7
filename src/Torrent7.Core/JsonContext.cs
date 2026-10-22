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
