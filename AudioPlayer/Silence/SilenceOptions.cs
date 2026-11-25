namespace AudioPlayer.Silence;

// Immutable, operation-local parameters. No dependency on application settings.
public sealed record SilenceOptions(
    int FrameMilliseconds = SilenceOptions.DefaultFrameMilliseconds,
    int MinimumSilenceMilliseconds = SilenceOptions.DefaultMinimumSilenceMilliseconds,
    int SilenceThresholdDb = SilenceOptions.DefaultSilenceThresholdDb,
    decimal AllowedSilenceSeconds = SilenceOptions.DefaultAllowedSilenceSeconds)
{
    public const int DefaultFrameMilliseconds = 10;
    public const int DefaultMinimumSilenceMilliseconds = 300;
    public const int DefaultSilenceThresholdDb = -45;
    public const int DefaultNavigationToleranceMilliseconds = 200;
    public const decimal DefaultAllowedSilenceSeconds = 2.0m;

    public void Validate()
    {
        ValidateDetection();
        ValidateAllowedSilence();
    }

    internal void ValidateDetection()
    {
        if (FrameMilliseconds < 1 || FrameMilliseconds > 100)
            throw new ArgumentOutOfRangeException(nameof(FrameMilliseconds), "分析帧长度必须在 1 到 100 ms 之间。");
        if (MinimumSilenceMilliseconds < 50 || MinimumSilenceMilliseconds > 5000)
            throw new ArgumentOutOfRangeException(nameof(MinimumSilenceMilliseconds), "最短停顿时长必须在 50 到 5000 ms 之间。");
        if (SilenceThresholdDb < -100 || SilenceThresholdDb > 0)
            throw new ArgumentOutOfRangeException(nameof(SilenceThresholdDb), "静音阈值必须在 -100 到 0 dBFS 之间。");
    }

    internal void ValidateAllowedSilence()
    {
        if (AllowedSilenceSeconds < MinimumSilenceMilliseconds / 1000m
            || decimal.Round(AllowedSilenceSeconds, 1) != AllowedSilenceSeconds)
            throw new ArgumentOutOfRangeException(nameof(AllowedSilenceSeconds),
                $"允许静音时长不得低于 {Math.Ceiling(MinimumSilenceMilliseconds / 100m) / 10m:0.0} 秒，且最多一位小数。");
    }

    internal static void ValidateNavigationTolerance(int navigationToleranceMilliseconds)
    {
        if (navigationToleranceMilliseconds < 1 || navigationToleranceMilliseconds > 5000)
            throw new ArgumentOutOfRangeException("NavigationToleranceMilliseconds", "导航容差必须在 1 到 5000 ms 之间。");
    }
}
