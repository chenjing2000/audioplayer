using System.IO;
using System.Text.Json;

namespace AudioPlayer.Settings;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string FilePath { get; }

    public SettingsStore() : this(GetFilePath(GetExecutablePath()))
    {
    }

    public SettingsStore(string filePath) => FilePath = filePath;

    internal static string GetExecutablePath()
    {
        string? executablePath = Environment.ProcessPath;
        if (executablePath is null || Path.GetFileNameWithoutExtension(executablePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(AppContext.BaseDirectory, "AudioPlayer.exe");
        return executablePath;
    }

    public static string GetFilePath(string executablePath)
        => Path.ChangeExtension(executablePath, ".settings.json");

    public AppSettings LoadOrCreate()
    {
        if (!File.Exists(FilePath))
        {
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        string json = File.ReadAllText(FilePath);
        var settings = JsonSerializer.Deserialize<AppSettings>(json)
            ?? throw new JsonException("配置文件不能为 null。");
        using (var document = JsonDocument.Parse(json))
        {
            foreach (string name in new[]
            {
                nameof(AppSettings.FrameMilliseconds),
                nameof(AppSettings.MinimumSilenceMilliseconds),
                nameof(AppSettings.SilenceThresholdDb),
                nameof(AppSettings.NavigationToleranceMilliseconds),
                nameof(AppSettings.AllowedSilenceSeconds)
            })
            {
                if (!document.RootElement.TryGetProperty(name, out _))
                    throw new JsonException("配置文件缺少必填参数：" + name);
            }
        }
        if (settings.ScriptAppearance is not null)
            settings.ScriptAppearance = settings.ScriptAppearance.NormalizeRemovedOptions();
        settings.Validate();
        return settings;
    }

    public void Save(AppSettings settings)
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
