using NAudio.Wave;
using System.IO;

namespace AudioPausePlayer;

public sealed record SilenceTrimResult(
    string OutputPath,
    TimeSpan OriginalDuration,
    TimeSpan OutputDuration,
    int TrimmedRangeCount,
    TimeSpan RemovedDuration,
    bool OutputSilenceSaved);

internal readonly record struct SilenceCut(long SilenceStart, long CutStart, long CutEnd, long SilenceEnd);

public static class SilenceTrimmer
{
    public static Task<SilenceTrimResult> TrimAsync(string sourcePath, SilenceSettings settings,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Trim(sourcePath, settings, cancellationToken), cancellationToken);

    private static SilenceTrimResult Trim(string sourcePath, SilenceSettings settings, CancellationToken cancellationToken)
    {
        settings.Validate();
        string extension = Path.GetExtension(sourcePath);
        string type = extension.ToLowerInvariant();
        if (type is not (".wav" or ".mp3" or ".m4a"))
            throw new NotSupportedException("只支持 WAV、MP3 和 AAC/M4A 音频。");

        string source = Path.GetFullPath(sourcePath);
        string directory = Path.GetDirectoryName(source)!;
        string stem = Path.GetFileNameWithoutExtension(source);
        cancellationToken.ThrowIfCancellationRequested();
        SilenceAnalysis analysis = SilenceResultStore.LoadOrDetect(source, settings, cancellationToken);
        if (!analysis.Saved)
            throw new IOException("停顿信息无法写入 results 文件夹，请检查权限后重试。");
        int sampleRate = analysis.Fingerprint.SampleRate;
        TimeSpan duration = TimeSpan.FromTicks(analysis.Fingerprint.DurationTicks);

        // Use exactly the validated result of the original PCM detection.
        List<SilenceCut> cuts = PlanCuts(analysis.Ranges, sampleRate, settings.AllowedSilenceSeconds);
        if (cuts.Count == 0)
            throw new InvalidOperationException("没有超过允许时长的静音区间，无需裁剪。");
        long removedFrames = cuts.Sum(cut => cut.CutEnd - cut.CutStart);
        TimeSpan removed = TimeSpan.FromSeconds((double)removedFrames / sampleRate);
        double expectedSeconds = Math.Max(0, duration.TotalSeconds - removed.TotalSeconds);

        // The temporary extension is chosen for Media Foundation's container selection.
        string temporaryExtension = type == ".m4a" ? ".mp4" : type;
        string temporary = Path.Combine(directory,
            "." + stem + ".lesssilence." + Guid.NewGuid().ToString("N") + temporaryExtension);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var reader = new AudioFileReader(source))
            {
                var trimmed = new TrimSampleProvider(reader, cuts, cancellationToken);
                switch (type)
                {
                    case ".wav":
                        WaveFileWriter.CreateWaveFile16(temporary, trimmed);
                        break;
                    case ".mp3":
                        MediaFoundationEncoder.EncodeToMp3(trimmed.ToWaveProvider16(), temporary);
                        break;
                    case ".m4a":
                        MediaFoundationEncoder.EncodeToAac(trimmed.ToWaveProvider16(), temporary);
                        break;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan resultDuration = ValidateOutput(temporary, expectedSeconds, type);
            if (!SilenceResultStore.IsUnchanged(source, analysis.Fingerprint, cancellationToken))
                throw new IOException("音频在裁剪期间发生变化，输出已取消。");

            // Never overwrite. A competing creator of a candidate name simply makes us try the next.
            string destination;
            for (int index = 0; ; index++)
            {
                string suffix = index == 0 ? "" : " (" + index + ")";
                destination = Path.Combine(directory, stem + ".lesssilence" + suffix + extension);
                try
                {
                    File.Move(temporary, destination);
                    break;
                }
                catch (IOException) when (File.Exists(destination))
                {
                    if (index == int.MaxValue - 1)
                        throw new IOException("无法找到可用的裁剪输出文件名。");
                }
            }

            // The re-encoded output has a new PCM timeline and must be analyzed independently.
            bool outputSaved;
            try
            {
                outputSaved = SilenceResultStore.LoadOrDetect(destination, settings, cancellationToken).Saved;
            }
            catch (Exception)
            {
                outputSaved = false; // Do not remove successfully committed audio.
            }
            return new SilenceTrimResult(destination, duration, resultDuration,
                cuts.Count, removed, outputSaved);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static TimeSpan ValidateOutput(string temporary, double expectedSeconds, string type)
    {
        if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
            throw new InvalidDataException("输出文件为空或不存在。");
        using var check = new AudioFileReader(temporary);
        TimeSpan actual = check.TotalTime;
        double tolerance = type == ".wav" ? 0.02 : Math.Max(0.50, expectedSeconds * 0.005);
        if (!double.IsFinite(actual.TotalSeconds) || actual <= TimeSpan.Zero
            || Math.Abs(actual.TotalSeconds - expectedSeconds) > tolerance)
            throw new InvalidDataException(
                $"输出文件时长验证失败，预期 {expectedSeconds:F3} 秒，实际 {actual.TotalSeconds:F3} 秒。");
        return actual;
    }

    // Accessible to tests: convert input silence ranges into deletions in integer PCM frame units.
    internal static List<SilenceCut> PlanCuts(IReadOnlyList<SilenceRange> ranges, int sampleRate, decimal allowedSeconds)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (allowedSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(allowedSeconds));
        if (allowedSeconds >= (decimal)long.MaxValue / sampleRate) return new List<SilenceCut>();
        long allowedFrames = decimal.ToInt64(decimal.Round(allowedSeconds * sampleRate, 0,
            MidpointRounding.AwayFromZero));
        var cuts = new List<SilenceCut>();
        long previousEnd = 0;
        foreach (var range in ranges)
        {
            long start = checked((long)Math.Round(range.Start.TotalSeconds * sampleRate,
                MidpointRounding.AwayFromZero));
            long end = checked((long)Math.Round(range.End.TotalSeconds * sampleRate,
                MidpointRounding.AwayFromZero));
            if (start < previousEnd || end < start)
                throw new InvalidDataException("静音区间顺序或边界无效。");
            previousEnd = end;
            if (end - start <= allowedFrames) continue;
            long left = allowedFrames / 2;
            long right = allowedFrames - left;
            cuts.Add(new SilenceCut(start, start + left, end - right, end));
        }
        return cuts;
    }
}

// Reads the original audio exactly once, skipping cuts on the ORIGINAL timeline.
// No seeking, in-place editing, or cumulative time-offset adjustment is required.
internal sealed class TrimSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly IReadOnlyList<SilenceCut> cuts;
    private readonly CancellationToken cancellationToken;
    private readonly float[] readBuffer;
    private int buffered;
    private int consumed;
    private int cutIndex;
    private long frameIndex;
    private readonly int fadeFrames;

    public TrimSampleProvider(ISampleProvider source, IReadOnlyList<SilenceCut> cuts,
        CancellationToken cancellationToken)
    {
        this.source = source;
        this.cuts = cuts;
        this.cancellationToken = cancellationToken;
        WaveFormat = source.WaveFormat;
        readBuffer = new float[8192 * WaveFormat.Channels];
        fadeFrames = Math.Max(1, (int)Math.Round(WaveFormat.SampleRate * 0.010));
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int channels = WaveFormat.Channels;
        int capacity = count / channels * channels;
        int output = 0;
        while (output < capacity)
        {
            if (consumed >= buffered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                buffered = source.Read(readBuffer, 0, readBuffer.Length);
                consumed = 0;
                if (buffered == 0) break;
                if (buffered % channels != 0)
                    throw new InvalidDataException("音频采样包含不完整的声道组。");
            }

            int availableFrames = (buffered - consumed) / channels;
            int spaceFrames = (capacity - output) / channels;
            int take = Math.Min(availableFrames, spaceFrames);
            for (int frame = 0; frame < take; frame++)
            {
                long current = frameIndex++;
                while (cutIndex < cuts.Count && current >= cuts[cutIndex].SilenceEnd)
                    cutIndex++;

                float gain = 1f;
                if (cutIndex < cuts.Count)
                {
                    SilenceCut cut = cuts[cutIndex];
                    if (current >= cut.CutStart && current < cut.CutEnd)
                        continue;

                    if (current >= cut.SilenceStart && current < cut.CutStart)
                    {
                        int fade = (int)Math.Min(fadeFrames, cut.CutStart - cut.SilenceStart);
                        if (fade > 0 && current >= cut.CutStart - fade)
                            gain = fade == 1 ? 0f : (float)(cut.CutStart - 1 - current) / (fade - 1);
                    }
                    else if (current >= cut.CutEnd && current < cut.SilenceEnd)
                    {
                        int fade = (int)Math.Min(fadeFrames, cut.SilenceEnd - cut.CutEnd);
                        if (fade > 0 && current < cut.CutEnd + fade)
                            gain = fade == 1 ? 0f : (float)(current - cut.CutEnd) / (fade - 1);
                    }
                }

                int inputOffset = consumed + frame * channels;
                for (int channel = 0; channel < channels; channel++)
                    buffer[offset + output++] = readBuffer[inputOffset + channel] * gain;
            }
            consumed += take * channels;
        }
        return output;
    }
}
