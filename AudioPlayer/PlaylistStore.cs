using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioPlayer;

internal sealed class PlaylistStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string FilePath { get; }

    public PlaylistStore(string filePath) => FilePath = filePath;

    internal static string GetFilePath(string executablePath)
        => Path.ChangeExtension(executablePath, ".playlist.json");

    public PlaylistSnapshot? Load()
    {
        string json;
        try { json = File.ReadAllText(FilePath); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }

        var snapshot = JsonSerializer.Deserialize<PlaylistSnapshot>(json, Options)
            ?? throw new JsonException("播放列表不能为 null。");
        if (snapshot.Files is null || (snapshot.RootFolder is null && snapshot.Files.Length != 0)
            || (snapshot.RootFolder is not null && !Path.IsPathFullyQualified(snapshot.RootFolder))
            || snapshot.Files.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)))
            throw new JsonException("播放列表必须包含有效的绝对路径。");
        return snapshot;
    }

    public void Save(string? rootFolder, IEnumerable<string> files)
    {
        var snapshot = new PlaylistSnapshot(rootFolder, files.ToArray());
        string temporaryPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, Options));
            if (File.Exists(FilePath)) File.Replace(temporaryPath, FilePath, null);
            else File.Move(temporaryPath, FilePath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}

internal sealed record PlaylistSnapshot(
    [property: JsonRequired] string? RootFolder,
    [property: JsonRequired] string[] Files);
