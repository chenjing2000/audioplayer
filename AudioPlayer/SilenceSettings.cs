namespace AudioPlayer;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<PlaybackMode>))]
public enum PlaybackMode
{
    PlayOne, RepeatOne, Sequential, RepeatAll
}

public sealed class SilenceSettings
{
    public const int DefaultFrameMilliseconds = 10;
    public const int DefaultMinimumSilenceMilliseconds = 300;
    public const int DefaultSilenceThresholdDb = -45;
    public const int DefaultNavigationToleranceMilliseconds = 200;
    public const decimal DefaultAllowedSilenceSeconds = 2.0m;

    public int FrameMilliseconds { get; set; } = DefaultFrameMilliseconds;
    public int MinimumSilenceMilliseconds { get; set; } = DefaultMinimumSilenceMilliseconds;
    public int SilenceThresholdDb { get; set; } = DefaultSilenceThresholdDb;
    public int NavigationToleranceMilliseconds { get; set; } = DefaultNavigationToleranceMilliseconds;
    public decimal AllowedSilenceSeconds { get; set; } = DefaultAllowedSilenceSeconds;

    public const int DefaultBackwardStepSeconds = 5;
    public const int DefaultForwardStepSeconds = 4;
    public int BackwardStepSeconds { get; set; } = DefaultBackwardStepSeconds;
    public int ForwardStepSeconds { get; set; } = DefaultForwardStepSeconds;

    public string ExcludedFolderNames { get; set; } = "";
    public PlaybackMode PlaybackMode { get; set; } = PlaybackMode.PlayOne;
    public ScriptAppearance ScriptAppearance { get; set; } = new();

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
        if (FrameMilliseconds < 1 || FrameMilliseconds > 100)
            throw new ArgumentOutOfRangeException(nameof(FrameMilliseconds), "分析帧长度必须在 1 到 100 ms 之间。");
        if (MinimumSilenceMilliseconds < 50 || MinimumSilenceMilliseconds > 5000)
            throw new ArgumentOutOfRangeException(nameof(MinimumSilenceMilliseconds), "最短停顿时长必须在 50 到 5000 ms 之间。");
        if (SilenceThresholdDb < -100 || SilenceThresholdDb > 0)
            throw new ArgumentOutOfRangeException(nameof(SilenceThresholdDb), "静音阈值必须在 -100 到 0 dBFS 之间。");
        if (NavigationToleranceMilliseconds < 1 || NavigationToleranceMilliseconds > 5000)
            throw new ArgumentOutOfRangeException(nameof(NavigationToleranceMilliseconds), "导航容差必须在 1 到 5000 ms 之间。");
        if (AllowedSilenceSeconds < MinimumSilenceMilliseconds / 1000m
            || decimal.Round(AllowedSilenceSeconds, 1) != AllowedSilenceSeconds)
            throw new ArgumentOutOfRangeException(nameof(AllowedSilenceSeconds),
                $"允许静音时长不得低于 {Math.Ceiling(MinimumSilenceMilliseconds / 100m) / 10m:0.0} 秒，且最多一位小数。");
        return excludedNames;
    }
}

public sealed record ScriptAppearance
{
    internal static readonly string[] Fonts = ["Georgia", "Microsoft YaHei UI", "Nunito", "Times New Roman", "Arial"];
    internal static readonly int[] Sizes = [17, 18, 19, 20];
    internal static readonly double[] Heights = [1.5, 1.6, 1.7, 1.8];
    internal static readonly int[] Spacings = [8, 12, 16, 20];
    internal static readonly int[] Indents = [0, 1, 2];
    internal static readonly string[] Colors = ["#283747", "#344054", "#3F4750", "#315247", "#513D4D"];
    internal static readonly string[] PlaybackColors = ["#B64550", "#A45A2C", "#856300", "#53713C", "#26798A"];
    internal static readonly string[] Backgrounds = ["#F8F8F6", "#F0F3F7", "#F7F1E6", "#EFF5F0", "#F1F0F7"];

    public string Font { get; init; } = "Georgia";
    public int FontSize { get; init; } = 17;
    public double LineHeight { get; init; } = 1.6;
    public int ParagraphAfter { get; init; } = 16;
    public int FirstLineIndent { get; init; } = 2;
    public string Color { get; init; } = "#283747";
    public string ScriptColor { get; init; } = "#B64550";
    public string Background { get; init; } = "#F8F8F6";

    // Only formerly supported values are normalized on load. Unknown values
    // still fail validation, and loading never writes back to the user's file.
    internal ScriptAppearance NormalizeRemovedOptions() => this with
    {
        Font = Font == "Segoe UI" ? "Arial" : Font,
        FontSize = FontSize switch { 14 or 15 or 16 => 17, 22 or 24 or 26 or 28 => 20, _ => FontSize },
        LineHeight = LineHeight switch { 1.4 => 1.5, 2.0 => 1.8, _ => LineHeight },
        ParagraphAfter = ParagraphAfter switch { 0 => 8, 14 => 16, 18 or 24 => 20, _ => ParagraphAfter },
        FirstLineIndent = FirstLineIndent == 3 ? 2 : FirstLineIndent,
        ScriptColor = ScriptColor == "#A33D30" ? "#B64550" : ScriptColor,
        Background = Background == "#FFFFFF" ? "#F8F8F6" : Background
    };

    internal void Validate()
    {
        if (!Fonts.Contains(Font) || !Sizes.Contains(FontSize) || !Heights.Contains(LineHeight) ||
            !Spacings.Contains(ParagraphAfter) || !Indents.Contains(FirstLineIndent) ||
            !Colors.Contains(Color) || !PlaybackColors.Contains(ScriptColor) || !Backgrounds.Contains(Background))
            throw new ArgumentOutOfRangeException(nameof(ScriptAppearance), "无效的原文显示参数。");
    }
}
