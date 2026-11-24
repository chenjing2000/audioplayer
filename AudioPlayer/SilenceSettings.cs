namespace AudioPlayer;

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

    public void Validate()
    {
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
    }
}
