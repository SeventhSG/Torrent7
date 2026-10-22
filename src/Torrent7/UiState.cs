using System.Text.Json;
using System.Text.Json.Serialization;
using Torrent7.Core;

namespace Torrent7;

/// <summary>
/// How the window was left: where it sat, how big it was, how tall the details panel was,
/// and how the table's columns were arranged and sorted. Restored on the next start.
/// </summary>
public sealed class UiState
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public double Width { get; set; } = 1240;
    public double Height { get; set; } = 780;
    public bool Maximized { get; set; }
    public double DetailsHeight { get; set; } = 290;
    public List<ColumnState> Columns { get; set; } = [];
    public string? SortColumn { get; set; }
    public bool SortDescending { get; set; }

    private static string FilePath => Path.Combine(AppPaths.DataDir, "ui.json");

    public static UiState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), UiJson.Default.UiState) ?? new UiState();
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new UiState();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, UiJson.Default.UiState));
        }
        catch (IOException ex) { Log.Warn("ui.json: " + ex.Message); }
    }
}

public sealed class ColumnState
{
    public string Header { get; set; } = "";
    public double Width { get; set; }
    public int DisplayIndex { get; set; }
    public bool Visible { get; set; } = true;
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(UiState))]
internal sealed partial class UiJson : JsonSerializerContext;
