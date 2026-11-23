using System.IO;
using System.Text.Json;

namespace AudioPausePlayer;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string FilePath { get; }

    public SettingsStore() : this(GetFilePath(GetExecutablePath()))
    {
    }

    public SettingsStore(string filePath) => FilePath = filePath;

    private static string GetExecutablePath()
    {
        string? executablePath = Environment.ProcessPath;
        if (executablePath is null || Path.GetFileNameWithoutExtension(executablePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(AppContext.BaseDirectory, "EnglishAudioPlayer.exe");
        return executablePath;
    }

    public static string GetFilePath(string executablePath)
        => Path.ChangeExtension(executablePath, ".settings.json");

    public SilenceSettings LoadOrCreate()
    {
        if (!File.Exists(FilePath))
        {
            var defaults = new SilenceSettings();
            Save(defaults);
            return defaults;
        }

        string json = File.ReadAllText(FilePath);
        var settings = JsonSerializer.Deserialize<SilenceSettings>(json)
            ?? throw new JsonException("配置文件不能为 null。");
        using (var document = JsonDocument.Parse(json))
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("配置文件必须是 JSON 对象。");
            if (!document.RootElement.TryGetProperty(nameof(SilenceSettings.AllowedSilenceSeconds), out _))
                settings.AllowedSilenceSeconds = Math.Max(SilenceSettings.DefaultAllowedSilenceSeconds,
                    settings.MinimumSilenceMilliseconds / 1000m);
        }
        settings.Validate();
        return settings;
    }

    public void Save(SilenceSettings settings)
    {
        settings.Validate();
        string temporaryPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            if (File.Exists(FilePath)) File.Replace(temporaryPath, FilePath, null);
            else File.Move(temporaryPath, FilePath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
