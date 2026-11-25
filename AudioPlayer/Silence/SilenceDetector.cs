using NAudio.Wave;
using System.IO;

namespace AudioPlayer.Silence;

public static class SilenceDetector
{
    public static List<SilenceRange> Detect(string filePath)
    {
        return Detect(filePath, new SilenceOptions());
    }

    public static List<SilenceRange> Detect(string filePath, SilenceOptions settings)
        => Detect(filePath, settings, CancellationToken.None);

    public static List<SilenceRange> Detect(string filePath, SilenceOptions settings, CancellationToken cancellationToken)
    {
        settings.Validate();
        using var reader = new AudioFileReader(filePath);
        return DetectSamples(reader, settings, cancellationToken);
    }

    internal static List<SilenceRange> DetectSamples(ISampleProvider source)
    {
        return DetectSamples(source, new SilenceOptions());
    }

    internal static List<SilenceRange> DetectSamples(ISampleProvider source, SilenceOptions settings)
        => DetectSamples(source, settings, CancellationToken.None);

    internal static List<SilenceRange> DetectSamples(ISampleProvider source, SilenceOptions settings, CancellationToken cancellationToken)
    {
        settings.Validate();
        int rate = source.WaveFormat.SampleRate;
        int channels = source.WaveFormat.Channels;
        int frameMilliseconds = settings.FrameMilliseconds;
        int minimumSilenceMilliseconds = settings.MinimumSilenceMilliseconds;
        int silenceThresholdDb = settings.SilenceThresholdDb;

        var buffer = new float[((rate * frameMilliseconds + 999) / 1000) * channels];
        var ranges = new List<SilenceRange>();
        long processedGroups = 0;
        long frameIndex = 0;
        long? silenceStart = null;
        long minimumGroups = ((long)rate * minimumSilenceMilliseconds + 999) / 1000;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Derive boundaries from the absolute frame index to avoid rounding drift.
            long nextBoundary = (++frameIndex * rate * frameMilliseconds) / 1000;
            int requested = checked((int)(nextBoundary - processedGroups) * channels);
            int count = 0;
            while (count < requested)
            {
                int read = source.Read(buffer, count, requested - count);
                if (read == 0) break;
                count += read;
            }
            if (count == 0) break;
            if (count % channels != 0) throw new InvalidDataException("PCM 数据包含不完整的声道采样组。");

            long start = processedGroups;
            processedGroups += count / channels;
            double sum = 0;
            for (int i = 0; i < count; i++) sum += (double)buffer[i] * buffer[i];
            double rms = Math.Sqrt(sum / count);
            double db = rms == 0 ? double.NegativeInfinity : 20.0 * Math.Log10(rms);

            if (db < silenceThresholdDb)
            {
                if (!silenceStart.HasValue) silenceStart = start;
            }
            else if (silenceStart.HasValue)
            {
                AddRange(ranges, silenceStart.Value, start, minimumGroups, rate);
                silenceStart = null;
            }
            if (count < requested) break;
        }
        if (silenceStart.HasValue) AddRange(ranges, silenceStart.Value, processedGroups, minimumGroups, rate);
        return ranges;
    }

    private static void AddRange(List<SilenceRange> ranges, long start, long end, long minimum, int rate)
    {
        if (end - start < minimum) return;
        ranges.Add(new SilenceRange
        {
            Start = TimeSpan.FromSeconds((double)start / rate),
            End = TimeSpan.FromSeconds((double)end / rate)
        });
    }

    public static TimeSpan? FindNext(List<SilenceRange> ranges, TimeSpan currentTime)
    {
        return FindNext(ranges, currentTime, SilenceOptions.DefaultNavigationToleranceMilliseconds);
    }

    public static TimeSpan? FindNext(List<SilenceRange> ranges, TimeSpan currentTime, int navigationToleranceMilliseconds)
    {
        SilenceOptions.ValidateNavigationTolerance(navigationToleranceMilliseconds);
        TimeSpan limit = currentTime + TimeSpan.FromMilliseconds(navigationToleranceMilliseconds);
        foreach (var range in ranges)
            if (range.Middle > limit) return range.Middle;
        return null;
    }

    public static TimeSpan? FindPrevious(List<SilenceRange> ranges, TimeSpan currentTime)
    {
        return FindPrevious(ranges, currentTime, SilenceOptions.DefaultNavigationToleranceMilliseconds);
    }

    public static TimeSpan? FindPrevious(List<SilenceRange> ranges, TimeSpan currentTime, int navigationToleranceMilliseconds)
    {
        SilenceOptions.ValidateNavigationTolerance(navigationToleranceMilliseconds);
        TimeSpan limit = currentTime - TimeSpan.FromMilliseconds(navigationToleranceMilliseconds);
        for (int i = ranges.Count - 1; i >= 0; i--)
            if (ranges[i].Middle < limit) return ranges[i].Middle;
        return null;
    }
}
