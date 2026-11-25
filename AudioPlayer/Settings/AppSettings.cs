using AudioPlayer.Playback;
using AudioPlayer.Script;
using AudioPlayer.Silence;

namespace AudioPlayer.Settings;

public sealed class AppSettings
{
    public int FrameMilliseconds { get; set; } = SilenceOptions.DefaultFrameMilliseconds;
    public int MinimumSilenceMilliseconds { get; set; } = SilenceOptions.DefaultMinimumSilenceMilliseconds;
    public int SilenceThresholdDb { get; set; } = SilenceOptions.DefaultSilenceThresholdDb;
    public int NavigationToleranceMilliseconds { get; set; } = SilenceOptions.DefaultNavigationToleranceMilliseconds;
    public decimal AllowedSilenceSeconds { get; set; } = SilenceOptions.DefaultAllowedSilenceSeconds;

    public const int DefaultBackwardStepSeconds = 5;
    public const int DefaultForwardStepSeconds = 4;
    public int BackwardStepSeconds { get; set; } = DefaultBackwardStepSeconds;
    public int ForwardStepSeconds { get; set; } = DefaultForwardStepSeconds;

    public string ExcludedFolderNames { get; set; } = "";
    public PlaybackMode PlaybackMode { get; set; } = PlaybackMode.PlayOne;
    public ScriptAppearance ScriptAppearance { get; set; } = new();

    internal SilenceOptions GetSilenceOptions() => new(
        FrameMilliseconds, MinimumSilenceMilliseconds, SilenceThresholdDb, AllowedSilenceSeconds);

    internal HashSet<string> GetExcludedFolderNames()
    {
        if (ExcludedFolderNames is null)
            throw new ArgumentOutOfRangeException(nameof(ExcludedFolderNames), "需排除的文件夹不能为 null。");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in ExcludedFolderNames.Split(new[] { ',', ';', '，', '；' },
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (name.Length > 255 || name.EndsWith('.') || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0
                || System.Text.RegularExpressions.Regex.IsMatch(name,
                    @"^(?:CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³]) *(?:\.|$)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                throw new ArgumentOutOfRangeException(nameof(ExcludedFolderNames), $"无效的文件夹名称：{name}");
            names.Add(name);
        }
        return names;
    }

    public void Validate() => ValidateAndGetExcludedFolderNames();

    internal HashSet<string> ValidateAndGetExcludedFolderNames()
    {
        if (ScriptAppearance is null) throw new ArgumentOutOfRangeException(nameof(ScriptAppearance));
        ScriptAppearance.Validate();
        var excludedNames = GetExcludedFolderNames();
        if (!Enum.IsDefined(PlaybackMode))
            throw new ArgumentOutOfRangeException(nameof(PlaybackMode), "无效的播放模式。");
        if (BackwardStepSeconds is < 0 or > 99)
            throw new ArgumentOutOfRangeException(nameof(BackwardStepSeconds), "后退步长必须在 0 到 99 s 之间。");
        if (ForwardStepSeconds is < 0 or > 99)
            throw new ArgumentOutOfRangeException(nameof(ForwardStepSeconds), "前进步长必须在 0 到 99 s 之间。");
        SilenceOptions options = GetSilenceOptions();
        options.ValidateDetection();
        SilenceOptions.ValidateNavigationTolerance(NavigationToleranceMilliseconds);
        options.ValidateAllowedSilence();
        return excludedNames;
    }
}
