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

    internal const int MinFrameMilliseconds = 1, MaxFrameMilliseconds = 100;
    internal const int MinMinimumSilenceMilliseconds = 50, MaxMinimumSilenceMilliseconds = 5000;
    internal const int MinSilenceThresholdDb = -100, MaxSilenceThresholdDb = 0;
    internal const int MinNavigationToleranceMilliseconds = 1, MaxNavigationToleranceMilliseconds = 5000;

    public void Validate()
    {
        ValidateDetection();
        ValidateAllowedSilence();
    }

    internal void ValidateDetection()
    {
        if (FrameMilliseconds is < MinFrameMilliseconds or > MaxFrameMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(FrameMilliseconds), $"分析帧长度必须在 {MinFrameMilliseconds} 到 {MaxFrameMilliseconds} ms 之间。");
        if (MinimumSilenceMilliseconds is < MinMinimumSilenceMilliseconds or > MaxMinimumSilenceMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(MinimumSilenceMilliseconds), $"最短停顿时长必须在 {MinMinimumSilenceMilliseconds} 到 {MaxMinimumSilenceMilliseconds} ms 之间。");
        if (SilenceThresholdDb is < MinSilenceThresholdDb or > MaxSilenceThresholdDb)
            throw new ArgumentOutOfRangeException(nameof(SilenceThresholdDb), $"静音阈值必须在 {MinSilenceThresholdDb} 到 {MaxSilenceThresholdDb} dBFS 之间。");
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
        if (navigationToleranceMilliseconds is < MinNavigationToleranceMilliseconds or > MaxNavigationToleranceMilliseconds)
            throw new ArgumentOutOfRangeException("NavigationToleranceMilliseconds", $"导航容差必须在 {MinNavigationToleranceMilliseconds} 到 {MaxNavigationToleranceMilliseconds} ms 之间。");
    }
}
