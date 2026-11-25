using NAudio.Wave;
using System.IO;
using System.Text.Json;

using AudioPlayer.Settings;

namespace AudioPlayer.Silence;

internal sealed record AudioFingerprint(
    string FileName, long SizeBytes, DateTime LastWriteTimeUtc,
    long DurationTicks, int SampleRate, int Channels);

internal sealed record SilenceAnalysis(
    IReadOnlyList<SilenceRange> Ranges, bool FromCache, bool Saved, AudioFingerprint Fingerprint);

// The single authoritative silence result for one audio file. Never reads legacy caches.
internal static class SilenceResultStore
{
    private const int SchemaVersion = 1;
    private const int AlgorithmVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private sealed record StoredRange(long StartTicks, long EndTicks);

    private sealed class StoredResult
    {
        public required int SchemaVersion { get; init; }
        public required int AlgorithmVersion { get; init; }
        public required string FileName { get; init; }
        public required long SizeBytes { get; init; }
        public required DateTime LastWriteTimeUtc { get; init; }
        public required long DurationTicks { get; init; }
        public required int SampleRate { get; init; }
        public required int Channels { get; init; }
        public required int FrameMilliseconds { get; init; }
        public required int MinimumSilenceMilliseconds { get; init; }
        public required int SilenceThresholdDb { get; init; }
        public required List<StoredRange> Ranges { get; init; }
    }

    public static string GetPath(string audioPath)
    {
        string full = Path.GetFullPath(audioPath);
        return Path.Combine(Path.GetDirectoryName(full)!, "results", Path.GetFileName(full) + ".silent_segments.json");
    }

    public static AudioFingerprint Capture(string audioPath, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        string full = Path.GetFullPath(audioPath);
        var before = new FileInfo(full);
        long length = before.Length;
        DateTime modified = before.LastWriteTimeUtc;
        using var reader = new AudioFileReader(full);
        var fingerprint = new AudioFingerprint(
            Path.GetFileName(full), length, modified, reader.TotalTime.Ticks,
            reader.WaveFormat.SampleRate, reader.WaveFormat.Channels);
        cancellation.ThrowIfCancellationRequested();
        var after = new FileInfo(full);
        if (length != after.Length || modified != after.LastWriteTimeUtc)
            throw new IOException("音频在读取期间发生变化，请重试。");
        return fingerprint;
    }

    public static bool IsUnchanged(string audioPath, AudioFingerprint expected, CancellationToken cancellation = default)
        => Capture(audioPath, cancellation) == expected;

    // Decoder sample counts may round one PCM sample past the container's reported duration.
    // Clamp only this sub-sample boundary difference; larger overflows remain invalid.
    private static long NormalizeEndTicks(long endTicks, AudioFingerprint fingerprint)
    {
        if (endTicks <= fingerprint.DurationTicks) return endTicks;
        long oneSampleTicks = (TimeSpan.TicksPerSecond + fingerprint.SampleRate - 1L) / fingerprint.SampleRate;
        return endTicks - fingerprint.DurationTicks <= oneSampleTicks ? fingerprint.DurationTicks : endTicks;
    }

    public static IReadOnlyList<SilenceRange>? TryLoad(string audioPath, SilenceSettings settings,
        CancellationToken cancellation = default)
        => TryLoadVerified(audioPath, settings, cancellation)?.Ranges;

    // Return the already-validated fingerprint to callers that need the sample rate.
    internal static SilenceAnalysis? TryLoadVerified(string audioPath, SilenceSettings settings,
        CancellationToken cancellation = default)
    {
        AudioFingerprint fingerprint = Capture(audioPath, cancellation);
        IReadOnlyList<SilenceRange>? ranges = TryLoad(audioPath, settings, fingerprint, cancellation);
        return ranges is null ? null : new SilenceAnalysis(ranges, true, true, fingerprint);
    }

    private static IReadOnlyList<SilenceRange>? TryLoad(string audioPath, SilenceSettings settings,
        AudioFingerprint fingerprint, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        string path = GetPath(audioPath);
        if (!File.Exists(path)) return null;

        StoredResult? stored;
        try
        {
            stored = JsonSerializer.Deserialize<StoredResult>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException) { return null; }
        if (stored is null
            || stored.SchemaVersion != SchemaVersion || stored.AlgorithmVersion != AlgorithmVersion
            || stored.FileName != fingerprint.FileName || stored.SizeBytes != fingerprint.SizeBytes
            || stored.LastWriteTimeUtc != fingerprint.LastWriteTimeUtc
            || stored.DurationTicks != fingerprint.DurationTicks
            || stored.SampleRate != fingerprint.SampleRate || stored.Channels != fingerprint.Channels
            || stored.FrameMilliseconds != settings.FrameMilliseconds
            || stored.MinimumSilenceMilliseconds != settings.MinimumSilenceMilliseconds
            || stored.SilenceThresholdDb != settings.SilenceThresholdDb
            || stored.Ranges is null)
            return null;

        var result = new List<SilenceRange>(stored.Ranges.Count);
        long previous = 0;
        foreach (StoredRange item in stored.Ranges)
        {
            if (item is null) return null;
            long endTicks = NormalizeEndTicks(item.EndTicks, fingerprint);
            if (item.StartTicks < previous || item.StartTicks < 0
                || endTicks <= item.StartTicks || endTicks > fingerprint.DurationTicks)
                return null;
            result.Add(new SilenceRange
            {
                Start = TimeSpan.FromTicks(item.StartTicks),
                End = TimeSpan.FromTicks(endTicks)
            });
            previous = endTicks;
        }
        cancellation.ThrowIfCancellationRequested();
        if (!IsUnchanged(audioPath, fingerprint, cancellation)) return null;
        return result;
    }

    public static SilenceAnalysis LoadOrDetect(string audioPath, SilenceSettings settings,
        CancellationToken cancellation = default)
    {
        settings.Validate();
        AudioFingerprint before = Capture(audioPath, cancellation);
        IReadOnlyList<SilenceRange>? cached = TryLoad(audioPath, settings, before, cancellation);
        if (cached is not null) return new SilenceAnalysis(cached, true, true, before);

        List<SilenceRange> detected = SilenceDetector.Detect(audioPath, settings, cancellation);
        if (!IsUnchanged(audioPath, before, cancellation))
            throw new IOException("音频在检测期间发生变化，结果未保存。");

        bool saved = TrySave(audioPath, before, settings, detected, cancellation);
        return new SilenceAnalysis(detected, false, saved, before);
    }

    private static bool TrySave(string audioPath, AudioFingerprint fingerprint, SilenceSettings settings,
        IReadOnlyList<SilenceRange> ranges, CancellationToken cancellation)
    {
        string path = GetPath(audioPath);
        string? directory = Path.GetDirectoryName(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            cancellation.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory!);
            var result = new StoredResult
            {
                SchemaVersion = SchemaVersion,
                AlgorithmVersion = AlgorithmVersion,
                FileName = fingerprint.FileName,
                SizeBytes = fingerprint.SizeBytes,
                LastWriteTimeUtc = fingerprint.LastWriteTimeUtc,
                DurationTicks = fingerprint.DurationTicks,
                SampleRate = fingerprint.SampleRate,
                Channels = fingerprint.Channels,
                FrameMilliseconds = settings.FrameMilliseconds,
                MinimumSilenceMilliseconds = settings.MinimumSilenceMilliseconds,
                SilenceThresholdDb = settings.SilenceThresholdDb,
                Ranges = ranges.Select(r => new StoredRange(r.Start.Ticks,
                    NormalizeEndTicks(r.End.Ticks, fingerprint))).ToList()
            };
            File.WriteAllText(temporary, JsonSerializer.Serialize(result, JsonOptions));
            cancellation.ThrowIfCancellationRequested();
            if (!IsUnchanged(audioPath, fingerprint, cancellation))
                throw new InvalidOperationException("音频在保存停顿信息前发生变化，检测结果已丢弃。");
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // The session may navigate, but trimming needs a persisted result.
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
