using AudioPlayer;
using PlayerEngine = global::AudioPlayer.PlaybackEngine;
using NAudio.Wave;
using System.IO;

int failures = 0;
int passed = 0;
void Check(string name, Action test)
{
    try { test(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex.Message); failures++; }
}
void Assert(bool value, string message = "Assertion failed")
{
    if (!value) throw new Exception(message);
}
List<SilenceRange> Detect(float[] samples, int rate = 1000, int channels = 1, int maxRead = int.MaxValue, SilenceSettings? settings = null)
{
    var source = new Samples(samples, rate, channels, maxRead);
    return settings is null ? SilenceDetector.DetectSamples(source) : SilenceDetector.DetectSamples(source, settings);
}
float[] Signal(int soundBefore, int silence, int soundAfter)
    => Enumerable.Repeat(0.5f, soundBefore).Concat(new float[silence]).Concat(Enumerable.Repeat(0.5f, soundAfter)).ToArray();


Check("Settings initialize beside executable, persist and preserve invalid files", () =>
{
    string folder = Path.Combine(AppContext.BaseDirectory, "settings-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        var store = new SettingsStore(Path.Combine(folder, "RenamedPlayer.settings.json"));
        Assert(!File.Exists(store.FilePath));
        var settings = store.LoadOrCreate();
        Assert(File.Exists(store.FilePath) && settings.FrameMilliseconds == 10);
        settings.FrameMilliseconds = 20;
        settings.MinimumSilenceMilliseconds = 500;
        settings.SilenceThresholdDb = -40;
        settings.NavigationToleranceMilliseconds = 250;
        store.Save(settings);
        var loaded = new SettingsStore(store.FilePath).LoadOrCreate();
        Assert(loaded.FrameMilliseconds == 20 && loaded.MinimumSilenceMilliseconds == 500
            && loaded.SilenceThresholdDb == -40 && loaded.NavigationToleranceMilliseconds == 250);
        string previous = File.ReadAllText(store.FilePath);
        settings.FrameMilliseconds = 0;
        try { store.Save(settings); throw new Exception("Invalid settings accepted"); }
        catch (ArgumentOutOfRangeException) { }
        Assert(File.ReadAllText(store.FilePath) == previous);
        foreach (string invalid in new[] { "{broken", "null", "{\"FrameMilliseconds\":0}" })
        {
            File.WriteAllText(store.FilePath, invalid);
            bool rejected = false;
            try { store.LoadOrCreate(); }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException) { rejected = true; }
            Assert(rejected && File.ReadAllText(store.FilePath) == invalid);
        }
        Assert(Directory.GetFiles(folder).Length == 1, "Temporary settings files left behind");
        Assert(SettingsStore.GetFilePath(Path.Combine(folder, "RenamedPlayer.exe")) == store.FilePath);
    }
    finally { Directory.Delete(folder, true); }
});

Check("Settings save failure preserves original configuration", () =>
{
    string folder = Path.Combine(AppContext.BaseDirectory, "readonly-settings-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    string path = Path.Combine(folder, "Player.settings.json");
    try
    {
        var store = new SettingsStore(path);
        store.LoadOrCreate();
        string original = File.ReadAllText(path);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        bool rejected = false;
        try { store.Save(new SilenceSettings { FrameMilliseconds = 30 }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { rejected = true; }
        Assert(rejected && File.ReadAllText(path) == original);
        Assert(Directory.GetFiles(folder).Length == 1);
    }
    finally
    {
        if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(folder, true);
    }
});

Check("Silence settings defaults", () =>
{
    var settings = new SilenceSettings();
    Assert(settings.FrameMilliseconds == 10);
    Assert(settings.MinimumSilenceMilliseconds == 300);
    Assert(settings.SilenceThresholdDb == -45);
    Assert(settings.NavigationToleranceMilliseconds == 200);
});
Check("Custom minimum silence is used", () =>
{
    var settings = new SilenceSettings { MinimumSilenceMilliseconds = 100 };
    Assert(Detect(new float[100], settings: settings).Count == 1);
});
Check("Custom silence threshold is used", () =>
{
    var settings = new SilenceSettings { SilenceThresholdDb = -50 };
    Assert(Detect(Enumerable.Repeat(0.005f, 400).ToArray(), settings: settings).Count == 0);
});
Check("Minimum threshold detects digital silence and very quiet samples", () =>
{
    var settings = new SilenceSettings { SilenceThresholdDb = -100 };
    Assert(Detect(new float[400], settings: settings).Count == 1);
    Assert(Detect(Enumerable.Repeat(1e-8f, 400).ToArray(), settings: settings).Count == 1);
    Assert(Detect(Enumerable.Repeat(0.0001f, 400).ToArray(), settings: settings).Count == 0);
});
Check("Cancel PCM silence scan", () =>
{
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    bool cancelled = false;
    try
    {
        SilenceDetector.DetectSamples(new Samples(new float[1000], 1000, 1, 1000),
            new SilenceSettings(), cancellation.Token);
    }
    catch (OperationCanceledException) { cancelled = true; }
    Assert(cancelled);
});
Check("299 ms is too short", () => Assert(Detect(new float[299]).Count == 0));
Check("300 ms qualifies at EOF", () => { var r = Detect(new float[300]); Assert(r.Count == 1 && r[0].Start == TimeSpan.Zero && r[0].End == TimeSpan.FromMilliseconds(300)); });
Check("No silence", () => Assert(Detect(Enumerable.Repeat(0.5f, 1000).ToArray()).Count == 0));
Check("Empty PCM", () => Assert(Detect(Array.Empty<float>()).Count == 0));
Check("Leading silence", () => { var r = Detect(Signal(0, 400, 100)); Assert(r.Count == 1 && r[0].End == TimeSpan.FromMilliseconds(400)); });
Check("Interior silence", () => { var r = Detect(Signal(100, 400, 100)); Assert(r.Count == 1 && r[0].Middle == TimeSpan.FromMilliseconds(300)); });
Check("Partial final frame", () => { var r = Detect(Signal(100, 305, 0)); Assert(r.Count == 1 && r[0].End == TimeSpan.FromMilliseconds(405)); });
Check("Short reads are not EOF", () => { var r = Detect(Signal(100, 400, 100), maxRead: 3); Assert(r.Count == 1 && r[0].Start == TimeSpan.FromMilliseconds(100) && r[0].End == TimeSpan.FromMilliseconds(500)); });
Check("Opposite stereo does not cancel", () => { float[] s = new float[1000]; for (int i = 0; i < s.Length; i++) s[i] = i % 2 == 0 ? 0.5f : -0.5f; Assert(Detect(s, channels: 2).Count == 0); });
Check("Stereo duration", () => { var r = Detect(new float[800], channels: 2, maxRead: 3); Assert(r.Count == 1 && r[0].End == TimeSpan.FromMilliseconds(400)); });
Check("Non integral frame sampling rate", () => { var r = Detect(new float[11025], rate: 11025, maxRead: 17); Assert(r.Count == 1 && r[0].End == TimeSpan.FromSeconds(1)); });
Check("Low energy below threshold", () => Assert(Detect(Enumerable.Repeat(0.005f, 400).ToArray()).Count == 1));
Check("Energy above threshold", () => Assert(Detect(Enumerable.Repeat(0.006f, 400).ToArray()).Count == 0));
Check("Allowed silence default and linked minimum", () =>
{
    var settings = new SilenceSettings();
    Assert(settings.AllowedSilenceSeconds == 2.0m);
    settings.MinimumSilenceMilliseconds = 5000;
    bool rejected = false;
    try { settings.Validate(); }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Assert(rejected);
    settings.AllowedSilenceSeconds = 5.0m;
    settings.Validate();
    settings.AllowedSilenceSeconds = 5.01m;
    rejected = false;
    try { settings.Validate(); }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Assert(rejected);
});
Check("Missing trim value is not migrated and invalid precision is rejected", () =>
{
    string path = Path.Combine(Path.GetTempPath(), "legacy-audio-" + Guid.NewGuid().ToString("N") + ".json");
    try
    {
        File.WriteAllText(path, """{"FrameMilliseconds":10,"MinimumSilenceMilliseconds":300,"SilenceThresholdDb":-45,"NavigationToleranceMilliseconds":200}""");
        bool missingRejected = false;
        try { new SettingsStore(path).LoadOrCreate(); }
        catch (System.Text.Json.JsonException) { missingRejected = true; }
        Assert(missingRejected, "Do not silently fill a missing allowed-silence field");
        File.WriteAllText(path, """{"FrameMilliseconds":10,"MinimumSilenceMilliseconds":300,"SilenceThresholdDb":-45,"NavigationToleranceMilliseconds":200,"AllowedSilenceSeconds":1.25}""");
        bool precisionRejected = false;
        try { new SettingsStore(path).LoadOrCreate(); }
        catch (ArgumentOutOfRangeException) { precisionRejected = true; }
        Assert(precisionRejected, "Old precision is not migrated");
        File.WriteAllText(path, """{"FrameMilliseconds":10,"MinimumSilenceMilliseconds":3000,"SilenceThresholdDb":-45,"NavigationToleranceMilliseconds":200,"AllowedSilenceSeconds":2}""");
        bool rejected = false;
        try { new SettingsStore(path).LoadOrCreate(); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Assert(rejected);
    }
    finally { if (File.Exists(path)) File.Delete(path); }
});
Check("Silence results reuse verified metadata and ignore navigation and trim parameters", () =>
{
    string folder = Path.Combine(Path.GetTempPath(), "silence-result-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    string source = Path.Combine(folder, "lesson (1).wav");
    try
    {
        WaveFileWriter.CreateWaveFile16(source, new Samples(Signal(100, 4500, 100), 1000, 1, int.MaxValue));
        var settings = new SilenceSettings();
        string path = SilenceResultStore.GetPath(source);
        Assert(path == Path.Combine(folder, "results", "lesson (1).wav.silent_segments.json"));
        // Old nested results are not imported or changed by the new flat result store.
        string legacy = Path.Combine(folder, "results", "lesson (1).wav", "silence.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, "{old-cache}");
        var first = SilenceResultStore.LoadOrDetect(source, settings);
        Assert(File.ReadAllText(legacy) == "{old-cache}", "Legacy result remains untouched");
        Assert(!first.FromCache && first.Saved && first.Ranges.Count == 1 && File.Exists(path));
        string original = File.ReadAllText(path);
        Assert(!original.Contains("sha256", StringComparison.OrdinalIgnoreCase), "No content hashing");

        var reused = SilenceResultStore.LoadOrDetect(source, new SilenceSettings
        {
            NavigationToleranceMilliseconds = 500, AllowedSilenceSeconds = 3.0m
        });
        Assert(reused.FromCache && reused.Ranges.Count == 1);
        var verified = SilenceResultStore.TryLoadVerified(source, settings);
        Assert(verified is not null && verified.FromCache && verified.Saved
            && verified.Fingerprint.SampleRate == 1000
            && verified.Ranges.Count == 1,
            "Verified cache exposes its original metadata without re-opening the reader");
        Assert(File.ReadAllText(path) == original, "Cache hit did not rewrite JSON");

        settings.FrameMilliseconds = 20;
        Assert(!SilenceResultStore.LoadOrDetect(source, settings).FromCache, "Detection parameters invalidate cache");
        string newJson = File.ReadAllText(path);
        Assert(newJson != original && Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 1);

        File.WriteAllText(path, "{broken");
        Assert(!SilenceResultStore.LoadOrDetect(source, settings).FromCache, "Corrupt cache regenerates");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        node["ranges"]![0]!["endTicks"] = node["durationTicks"]!.GetValue<long>() + 10001;
        File.WriteAllText(path, node.ToJsonString());
        Assert(!SilenceResultStore.LoadOrDetect(source, settings).FromCache, "Out-of-bounds range regenerates");

        File.SetLastWriteTimeUtc(source, File.GetLastWriteTimeUtc(source).AddSeconds(4));
        Assert(!SilenceResultStore.LoadOrDetect(source, settings).FromCache, "Metadata change invalidates");
        Assert(SilenceResultStore.LoadOrDetect(source, settings).FromCache, "New result is reusable");
        Assert(!Directory.Exists(Path.Combine(folder, "cache")), "No legacy cache directory created");
    }
    finally { Directory.Delete(folder, true); }
});

Check("One PCM sample of decoder duration rounding keeps cached silence usable", () =>
{
    string folder = Path.Combine(Path.GetTempPath(), "silence-eof-rounding-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    string source = Path.Combine(folder, "end-silence.wav");
    try
    {
        using (var writer = new WaveFileWriter(source, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)))
        {
            float[] audio = new float[48000 * 3];
            writer.WriteSamples(audio, 0, audio.Length);
        }

        var settings = new SilenceSettings();
        var detected = SilenceResultStore.LoadOrDetect(source, settings);
        Assert(detected.Saved && detected.Ranges.Count == 1, "End-of-file silence was detected");

        string path = SilenceResultStore.GetPath(source);
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        long durationTicks = node["durationTicks"]!.GetValue<long>();
        Assert(node["ranges"]![0]!["endTicks"]!.GetValue<long>() == durationTicks,
            "New results store normalized PCM end boundaries");

        // Reproduce the supplied 48-kHz M4A result: last end 208 ticks past duration.
        node["ranges"]![0]!["endTicks"] = durationTicks + 208;
        File.WriteAllText(path, node.ToJsonString());
        var reused = SilenceResultStore.LoadOrDetect(source, settings);
        Assert(reused.FromCache && reused.Ranges.Count == 1
            && reused.Ranges[0].End.Ticks == durationTicks,
            "A 208-tick EOF rounding difference must not trigger re-detection");

        node["ranges"]![0]!["endTicks"] = durationTicks + 210;
        File.WriteAllText(path, node.ToJsonString());
        Assert(SilenceResultStore.TryLoad(source, settings) is null,
            "More than one sample beyond duration must remain invalid");
    }
    finally { Directory.Delete(folder, true); }
});

Check("Uncompressible silence does not create an output file", () =>
{
    string folder = Path.Combine(Path.GetTempPath(), "silence-short-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    string path = Path.Combine(folder, "short.wav");
    try
    {
        WaveFileWriter.CreateWaveFile16(path, new Samples(Signal(100, 700, 100), 1000, 1, int.MaxValue));
        bool rejected = false;
        try { SilenceTrimmer.TrimAsync(path, new SilenceSettings()).GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { rejected = true; }
        Assert(rejected && Directory.GetFiles(folder).Length == 1);
    }
    finally { Directory.Delete(folder, true); }
});

Check("Trim WAV output is separately saved, validated, and never overwritten", () =>
{
    string folder = Path.Combine(Path.GetTempPath(), "audio-trim-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    string source = Path.Combine(folder, "sample.wav");
    try
    {
        float[] input = Signal(100, 7000, 100);
        WaveFileWriter.CreateWaveFile16(source, new Samples(input, 1000, 1, int.MaxValue));
        long originalLength = new FileInfo(source).Length;
        SilenceTrimResult trimmed = SilenceTrimmer.TrimAsync(source, new SilenceSettings()).GetAwaiter().GetResult();
        Assert(trimmed.TrimmedRangeCount == 1 && Math.Abs(trimmed.RemovedDuration.TotalSeconds - 5) < 0.01);
        Assert(trimmed.OutputPath == Path.Combine(folder, "sample.new.wav"));
        Assert(new FileInfo(source).Length == originalLength, "Original source modified");
        using (var output = new AudioFileReader(trimmed.OutputPath))
            Assert(Math.Abs(output.TotalTime.TotalSeconds - 2.2) < 0.02);
        Assert(trimmed.OutputSilenceSaved, "Re-encoded output must have its own silence result");
        Assert(File.Exists(SilenceResultStore.GetPath(source)), "Source has independent silent_segments.json");
        Assert(File.Exists(SilenceResultStore.GetPath(trimmed.OutputPath)), "Output has independent silent_segments.json");
        var second = SilenceTrimmer.TrimAsync(source, new SilenceSettings()).GetAwaiter().GetResult();
        var third = SilenceTrimmer.TrimAsync(source, new SilenceSettings()).GetAwaiter().GetResult();
        Assert(second.OutputPath == Path.Combine(folder, "sample.new (1).wav"));
        Assert(third.OutputPath == Path.Combine(folder, "sample.new (2).wav"));
        Assert(second.OutputSilenceSaved && third.OutputSilenceSaved && Directory.GetFiles(folder).Length == 4,
            "Numbered files coexist with independent results");
        File.Delete(second.OutputPath);
        var reusedSlot = SilenceTrimmer.TrimAsync(source, new SilenceSettings()).GetAwaiter().GetResult();
        Assert(reusedSlot.OutputPath == second.OutputPath && reusedSlot.OutputSilenceSaved,
            "Smallest available numbered slot is used without preserving stale coordinates");
    }
    finally { Directory.Delete(folder, true); }
});
Check("FFmpeg concat list retains original time ranges and escapes apostrophes", () =>
{
    var cuts = new List<SilenceCut>
    {
        new(1000, 2000, 7000, 8000),
        new(9000, 10000, 15000, 16000)
    };
    string source = Path.Combine(Path.GetTempPath(), "O'Brien 课程.m4a");
    string text = FfmpegM4aTrimmer.BuildConcat(source, cuts, 1000, 18);
    Assert(text.StartsWith("ffconcat version 1.0\n"));
    Assert(text.Contains("O'\\''Brien 课程.m4a"));
    Assert(text.Contains("inpoint 0\noutpoint 2\n"));
    Assert(text.Contains("inpoint 7\noutpoint 10\n"));
    Assert(text.Contains("inpoint 15\noutpoint 18\n"));
    Assert(text.Split("file '").Length == 4);
    Assert(FfmpegM4aTrimmer.ExecutablePath.EndsWith(
        Path.Combine("runtimes", "ffmpeg", "win-x64", "bin", "ffmpeg.exe")));
});
Check("Missing FFmpeg reports the setup TXT without invoking an encoder", () =>
{
    if (File.Exists(FfmpegM4aTrimmer.ExecutablePath)) return; // local machine may have optional tools
    string folder = Path.Combine(Path.GetTempPath(), "missing-ffmpeg-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        string source = Path.Combine(folder, "source.m4a");
        bool missing = false;
        try
        {
            FfmpegM4aTrimmer.Trim(source, Path.Combine(folder, "out.m4a"),
                new List<SilenceCut>(), 48000, 2, CancellationToken.None);
        }
        catch (FfmpegUnavailableException ex)
        {
            missing = ex.Message.Contains("FFmpeg-Setup.txt");
        }
        Assert(missing && Directory.GetFiles(folder).Length == 0);
    }
    finally { Directory.Delete(folder, true); }
});

Check("Trim plan keeps equal sides and drops only middle", () =>
{
    var silence = new List<SilenceRange> { new() { Start = TimeSpan.FromSeconds(1), End = TimeSpan.FromSeconds(8) } };
    var cuts = SilenceTrimmer.PlanCuts(silence, 1000, 2.000m);
    Assert(cuts.Count == 1 && cuts[0].SilenceStart == 1000
        && cuts[0].CutStart == 2000 && cuts[0].CutEnd == 7000 && cuts[0].SilenceEnd == 8000);
    Assert(SilenceTrimmer.PlanCuts(silence, 1000, 7.000m).Count == 0);
    Assert(SilenceTrimmer.PlanCuts(silence, 1000, 8.000m).Count == 0);
});
Check("Trim output keeps all speech and frame count", () =>
{
    const int rate = 1000;
    float[] samples = Signal(100, 7000, 100);
    var silence = Detect(samples, rate);
    var cuts = SilenceTrimmer.PlanCuts(silence, rate, 2m);
    var provider = new TrimSampleProvider(new Samples(samples, rate, 1, 8192), cuts, CancellationToken.None);
    var result = new List<float>();
    float[] block = new float[257];
    int read;
    while ((read = provider.Read(block, 0, block.Length)) > 0)
        result.AddRange(block.Take(read));
    Assert(result.Count == 2200, "Unexpected output sample length");
    Assert(result.Take(100).All(x => x == 0.5f));
    Assert(result.Skip(2100).All(x => x == 0.5f));
});
Check("Fade reaches zero at both sides of the join", () =>
{
    float[] input = Enumerable.Repeat(0.001f, 7000).ToArray();
    var ranges = new List<SilenceRange> { new() { Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(7) } };
    var cuts = SilenceTrimmer.PlanCuts(ranges, 1000, 2m);
    var provider = new TrimSampleProvider(new Samples(input, 1000, 1, 8192), cuts, CancellationToken.None);
    float[] result = new float[2000];
    int written = 0, n;
    while (written < result.Length && (n = provider.Read(result, written, result.Length - written)) > 0) written += n;
    Assert(written == 2000);
    Assert(result[989] > result[995] && result[999] == 0f);
    Assert(result[1000] == 0f && result[1005] > result[1001] && result[1010] > 0f);
});
var ranges = new List<SilenceRange> { new() { Start = TimeSpan.FromSeconds(1), End = TimeSpan.FromSeconds(2) }, new() { Start = TimeSpan.FromSeconds(3), End = TimeSpan.FromSeconds(4) } };
Check("Next excludes exactly 200 ms", () => Assert(SilenceDetector.FindNext(ranges, TimeSpan.FromMilliseconds(1300)) == TimeSpan.FromMilliseconds(3500)));
Check("Next outside tolerance", () => Assert(SilenceDetector.FindNext(ranges, TimeSpan.FromMilliseconds(1299)) == TimeSpan.FromMilliseconds(1500)));
Check("Previous excludes exactly 200 ms", () => Assert(SilenceDetector.FindPrevious(ranges, TimeSpan.FromMilliseconds(3700)) == TimeSpan.FromMilliseconds(1500)));
Check("Previous outside tolerance", () => Assert(SilenceDetector.FindPrevious(ranges, TimeSpan.FromMilliseconds(3701)) == TimeSpan.FromMilliseconds(3500)));

Check("Custom navigation tolerance is used", () =>
{
    var settings = new SilenceSettings { NavigationToleranceMilliseconds = 1 };
    Assert(SilenceDetector.FindNext(ranges, TimeSpan.FromMilliseconds(1300), settings) == TimeSpan.FromMilliseconds(1500));
    Assert(SilenceDetector.FindPrevious(ranges, TimeSpan.FromMilliseconds(1700), settings) == TimeSpan.FromMilliseconds(1500));
});
Check("Navigation tolerance must be positive", () =>
{
    var settings = new SilenceSettings { NavigationToleranceMilliseconds = 0 };
    bool failed = false;
    try { settings.Validate(); }
    catch (ArgumentOutOfRangeException) { failed = true; }
    Assert(failed);
});
Check("No next retains position", () => Assert(SilenceDetector.FindNext(ranges, TimeSpan.FromSeconds(4)) is null));
Check("No previous retains position", () => Assert(SilenceDetector.FindPrevious(ranges, TimeSpan.Zero) is null));
Check("Empty navigation", () =>
{
    var empty = new List<SilenceRange>();
    Assert(SilenceDetector.FindNext(empty, TimeSpan.Zero) is null && SilenceDetector.FindPrevious(empty, TimeSpan.Zero) is null);
});

string audioFolder = Path.GetFullPath(Environment.GetEnvironmentVariable("AUDIOPLAYER_TEST_AUDIO_DIRECTORY") ?? "audio");
string? sampleMp3 = Directory.Exists(audioFolder)
    ? Directory.GetFiles(audioFolder, "*.mp3").FirstOrDefault()
    : null;

string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
try
{
    foreach (string name in new[] { "10.mp3", "2.MP3", "01.mp3", "3.mp3" })
        File.WriteAllText(Path.Combine(fixture, name), "not an mp3");

    Directory.CreateDirectory(Path.Combine(fixture, "nested"));
    File.WriteAllText(Path.Combine(fixture, "nested", "4.mp3"), "not an mp3");

    Directory.CreateDirectory(Path.Combine(fixture, "Book 9"));
    Directory.CreateDirectory(Path.Combine(fixture, "Book 10", "deep"));
    File.WriteAllText(Path.Combine(fixture, "Book 9", "2.wav"), "broken WAV fixture");
    File.WriteAllText(Path.Combine(fixture, "Book 10", "deep", "2.m4a"), "broken M4A fixture");
    File.WriteAllText(Path.Combine(fixture, "nested", "ignore.txt"), "not audio");

    Check("Recursive scan, supported extensions and natural relative-path order", () =>
    {
        var tracks = AudioLibrary.LoadFolder(fixture);
        Assert(tracks.Select(t => Path.GetRelativePath(fixture, t.FilePath)).SequenceEqual(new[]
        {
            "01.mp3", "2.MP3", "3.mp3", "10.mp3",
            Path.Combine("Book 9", "2.wav"), Path.Combine("Book 10", "deep", "2.m4a"),
            Path.Combine("nested", "4.mp3")
        }));
        Assert(tracks.All(t => t.DisplayName == Path.GetFileNameWithoutExtension(t.FilePath)));
        Assert(tracks.All(t => t.RowToolTip.StartsWith(Path.GetDirectoryName(t.FilePath)!))); 
        var tooltipTrack = new AudioTrack { FilePath = Path.Combine(fixture, "媒体文件.mp3"), Title = "Tag title" };
        Assert(tooltipTrack.RowToolTip == fixture);
    });

    Check("Playlist JSON uses executable stem, preserves full paths and replaces safely", () =>
    {
        string path = PlaylistStore.GetFilePath(Path.Combine(fixture, "Renamed Player.exe"));
        var store = new PlaylistStore(path);
        Assert(Path.GetFileName(path) == "Renamed Player.playlist.json" && store.Load() is null);
        var files = AudioLibrary.LoadFolder(fixture).Select(t => t.FilePath).ToArray();
        store.Save(fixture, files);
        var saved = store.Load()!;
        Assert(saved.RootFolder == fixture && saved.Files.SequenceEqual(files));
        using (var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)))
            Assert(json.RootElement.GetProperty("rootFolder").GetString() == fixture
                && json.RootElement.GetProperty("files").GetArrayLength() == files.Length);
        var restored = AudioLibrary.LoadPaths(fixture, files.Concat(new[] { files[0], Path.Combine(fixture, "..", "outside.wav") }));
        Assert(restored.Count == files.Length);
        store.Save(null, Array.Empty<string>());
        Assert(store.Load() is { RootFolder: null, Files.Length: 0 });
        Assert(!Directory.EnumerateFiles(fixture, "*.tmp").Any());
        foreach (string invalid in new[] { "{broken", "null", "{}", "{\"rootFolder\":null,\"files\":[\"relative.mp3\"]}" })
        {
            File.WriteAllText(path, invalid);
            bool rejected = false;
            try { store.Load(); }
            catch (System.Text.Json.JsonException) { rejected = true; }
            Assert(rejected && File.ReadAllText(path) == invalid);
        }
        string chineseRoot = Path.Combine(fixture, "英语学习");
        string chineseFile = Path.Combine(chineseRoot, "剑桥雅思.mp3");
        store.Save(chineseRoot, new[] { chineseFile });
        string readableJson = File.ReadAllText(path);
        Assert(readableJson.Contains("英语学习") && readableJson.Contains("剑桥雅思") &&
            store.Load()!.Files.SequenceEqual(new[] { chineseFile }));
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(
            new { rootFolder = chineseRoot, files = new[] { chineseFile } }));
        Assert(store.Load()!.Files.SequenceEqual(new[] { chineseFile }));
        File.Delete(path);
    });

    Check("Folder scans ignore only in-progress trim audio", () =>
    {
        string id = Guid.NewGuid().ToString("N");
        foreach (string extension in new[] { ".wav", ".mp3", ".m4a" })
            File.WriteAllText(Path.Combine(fixture, "nested", ".lesson.new." + id + extension), "unfinished");
        string ordinary = "lesson.new." + id + ".wav";
        string unusual = ".lesson.new.not-a-guid.wav";
        File.WriteAllText(Path.Combine(fixture, ordinary), "ordinary audio name");
        File.WriteAllText(Path.Combine(fixture, unusual), "dot-prefixed audio name");

        var names = AudioLibrary.LoadFolder(fixture)
            .Select(track => Path.GetFileName(track.FilePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert(!names.Contains(".lesson.new." + id + ".wav")
            && !names.Contains(".lesson.new." + id + ".mp3")
            && !names.Contains(".lesson.new." + id + ".m4a")
            && names.Contains(ordinary) && names.Contains(unusual));
    });

    Check("Broken file retained with error and unknown duration", () =>
    {
        var track = AudioLibrary.LoadFolder(fixture).Single(t => Path.GetFileName(t.FilePath) == "3.mp3");
        Assert(track.LoadError is not null && track.Duration is null && track.Title == "3" && track.DurationText == "未知");
    });

    Check("Metadata readers release broken files", () =>
    {
        AudioLibrary.LoadFolder(fixture);
        using var file = new FileStream(Path.Combine(fixture, "2.MP3"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert(file.Length > 0);
    });

    if (sampleMp3 is not null)
    {
        string valid = Path.Combine(fixture, "sample.mp3");
        File.Copy(sampleMp3, valid, true);

        using (var tags = TagLib.File.Create(valid))
        {
            tags.Tag.Title = "Test title";
            tags.Tag.Album = "Test album";
            tags.Save();
        }

        Check("Title and album metadata", () =>
        {
            var track = AudioLibrary.LoadFolder(fixture).Single(t => Path.GetFileName(t.FilePath) == "sample.mp3");
            Assert(track.Title == "Test title" && track.Album == "Test album" && track.Duration > TimeSpan.Zero);
        });

        using (var tags = TagLib.File.Create(valid))
        {
            tags.Tag.Title = " ";
            tags.Tag.Album = null;
            tags.Save();
        }

        Check("Missing metadata falls back", () =>
        {
            var track = AudioLibrary.LoadFolder(fixture).Single(t => Path.GetFileName(t.FilePath) == "sample.mp3");
            Assert(track.Title == "sample" && track.Album == "");
        });
    }
    else
    {
        Console.WriteLine("SKIP valid-MP3 metadata tests: add a local audio/*.mp3 file to enable them.");
    }

    Check("Missing folder reports error", () =>
    {
        try
        {
            AudioLibrary.LoadFolder(Path.Combine(fixture, "absent"));
            throw new Exception("No error");
        }
        catch (DirectoryNotFoundException)
        {
        }
    });
}
finally
{
    Directory.Delete(fixture, true);
}

Check("Active state follows playback flags", () =>
{
    var track = new AudioTrack { IsPaused = true };
    Assert(track.IsActive);
    track.IsPaused = false;
    Assert(!track.IsActive);
    track.IsPlaying = true;
    Assert(track.IsActive);
});

Check("Long duration formatting", () => Assert(AudioTrack.FormatTime(TimeSpan.FromSeconds(3661)) == "1:01:01"));

if (args.Contains("--report") && sampleMp3 is null)
    Console.WriteLine("SKIP --report: add local audio/*.mp3 files first.");
else if (args.Contains("--report"))
{
    string reportFolder = Path.GetFullPath("docs/test-results");
    Directory.CreateDirectory(reportFolder);
    var report = new System.Text.StringBuilder($"# 真实音频检测结果\n\n参数：{SilenceSettings.DefaultFrameMilliseconds} ms / "
        + $"{SilenceSettings.DefaultMinimumSilenceMilliseconds} ms / {SilenceSettings.DefaultSilenceThresholdDb} dBFS；"
        + $"导航容差 {SilenceSettings.DefaultNavigationToleranceMilliseconds} ms。\n\n"
        + "区间仅经自动数值检查，尚未经人工试听确认。\n\n");
    foreach (var track in AudioLibrary.LoadFolder(audioFolder))
    {
        Check("Real MP3: " + track.Title, () =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var found = SilenceDetector.Detect(track.FilePath);
            watch.Stop();
            using (var reader = new AudioFileReader(track.FilePath))
            {
                foreach (var r in found) Assert(r.Start >= TimeSpan.Zero && r.End <= reader.TotalTime + TimeSpan.FromMilliseconds(1) && r.End - r.Start >= TimeSpan.FromMilliseconds(SilenceSettings.DefaultMinimumSilenceMilliseconds));
                Assert(found.Count > 0);
            }
            using (var file = new FileStream(track.FilePath, FileMode.Open, FileAccess.Read, FileShare.None)) Assert(file.Length > 0);
            report.AppendLine($"## {Path.GetFileName(track.FilePath)}\n\nTitle：{track.Title}；Album：{track.Album}；标签时长：{track.Duration}；分析耗时：{watch.Elapsed.TotalMilliseconds:F1} ms；有效停顿：{found.Count}。\n");
            report.AppendLine("| 起点（秒） | 终点（秒） | 中点（秒） |\n|---:|---:|---:|");
            foreach (var r in found) report.AppendLine($"| {r.Start.TotalSeconds:F3} | {r.End.TotalSeconds:F3} | {r.Middle.TotalSeconds:F3} |");
            report.AppendLine();
        });
    }
    File.WriteAllText(Path.Combine(reportFolder, "silence-report.md"), report.ToString());
}

string? realTrimInput = Environment.GetEnvironmentVariable("AUDIOPLAYER_TEST_TRIM_FILE");
if (!string.IsNullOrWhiteSpace(realTrimInput))
{
    Check("Real WAV/MP3/M4A trim retains its source and output format", () =>
    {
        string source = Path.GetFullPath(realTrimInput);
        long initialLength = new FileInfo(source).Length;
        SilenceTrimResult trim = SilenceTrimmer.TrimAsync(source, new SilenceSettings()).GetAwaiter().GetResult();
        Assert(File.Exists(source) && File.Exists(trim.OutputPath));
        Assert(new FileInfo(source).Length == initialLength, "Original test input was changed");
        Assert(Path.GetExtension(trim.OutputPath).Equals(Path.GetExtension(source), StringComparison.OrdinalIgnoreCase));
        Assert(trim.TrimmedRangeCount > 0, "Real trimming input must contain compressible silence");
        Assert(trim.RemovedDuration >= TimeSpan.Zero);
        using var reader = new AudioFileReader(trim.OutputPath);
        Assert(reader.TotalTime > TimeSpan.Zero);
        Assert(Math.Abs((reader.TotalTime - trim.OutputDuration).TotalMilliseconds) < 10);
        Console.WriteLine($"REAL TRIM: {Path.GetFileName(source)}, sections={trim.TrimmedRangeCount}, " +
            $"removed={trim.RemovedDuration.TotalSeconds:F3}s, " +
            $"source={trim.OriginalDuration.TotalSeconds:F3}s, " +
            $"output={trim.OutputDuration.TotalSeconds:F3}s");
    });
}

try
{
    using var player = new PlayerEngine();
    await player.StopAsync(); await player.SeekAsync(TimeSpan.Zero); await player.PlayAsync(); player.Pause();
    Assert(!player.IsLoaded && player.PlaybackState == PlaybackState.Stopped);
    Console.WriteLine("PASS Unloaded playback actions"); passed++;
}
catch (Exception ex) { Console.WriteLine("FAIL Unloaded playback actions: " + ex.Message); failures++; }

if (args.Contains("--playback"))
{
    if (sampleMp3 is null)
    {
        Console.WriteLine("SKIP --playback: add local audio/*.mp3 files first.");
    }
    else
    {
        string playbackFixture = Path.Combine(AppContext.BaseDirectory, "playback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(playbackFixture);
        try
        {
            string path = Path.Combine(playbackFixture, "first.mp3");
            string secondPath = Path.Combine(playbackFixture, "second.mp3");
            File.Copy(sampleMp3, path);
            File.Copy(sampleMp3, secondPath);

            using var player = new PlayerEngine();
            await player.OpenAsync(path);
            Assert(player.IsLoaded && player.GetDuration() > TimeSpan.Zero);

            await player.PlayAsync();
            await Task.Delay(150);
            Assert(player.PlaybackState == PlaybackState.Playing);

            player.Pause();
            Assert(player.PlaybackState == PlaybackState.Paused);

            await player.SeekAsync(TimeSpan.FromSeconds(2));
            Assert(player.PlaybackState == PlaybackState.Paused && Math.Abs(player.GetCurrentTime().TotalSeconds - 2) < 0.1);

            await player.PlayAsync();
            await Task.Delay(100);
            await player.SeekAsync(TimeSpan.FromSeconds(4));
            Assert(player.PlaybackState == PlaybackState.Playing);

            await player.StopAsync();
            Assert(player.PlaybackState == PlaybackState.Stopped && player.GetCurrentTime() == TimeSpan.Zero);

            await player.PlayAsync();
            await Task.WhenAll(player.StopAsync(), player.SeekAsync(TimeSpan.FromSeconds(2))).WaitAsync(TimeSpan.FromSeconds(5));
            Assert(player.PlaybackState == PlaybackState.Stopped && Math.Abs(player.GetCurrentTime().TotalSeconds - 2) < 0.1);
            Console.WriteLine("PASS Concurrent stop and seek are serialized");
            passed++;

            await player.SeekAsync(TimeSpan.FromSeconds(-1));
            Assert(player.GetCurrentTime() == TimeSpan.Zero);
            await player.SeekAsync(TimeSpan.MaxValue);
            Assert(player.GetCurrentTime() <= player.GetDuration());

            await player.OpenAsync(secondPath);
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert(file.Length > 0);

            await player.CloseAsync();
            Assert(!player.IsLoaded);
            Console.WriteLine("PASS Playback lifecycle, seek and file release");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL Playback integration: " + ex);
            failures++;
        }
        finally
        {
            Directory.Delete(playbackFixture, true);
        }
    }
}

if (args.Contains("--ui"))
{
    {
        string uiFixture = Path.Combine(AppContext.BaseDirectory, "ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(uiFixture);
        try
        {
            if (sampleMp3 is not null)
            {
                File.Copy(sampleMp3, Path.Combine(uiFixture, "01.mp3"));
                File.Copy(sampleMp3, Path.Combine(uiFixture, "02.mp3"));
                File.Copy(sampleMp3, Path.Combine(uiFixture, "03.mp3"));
            }
            failures += await UiChecks.RunAsync(uiFixture);
        }
        finally
        {
            Directory.Delete(uiFixture, true);
        }
    }
}

Console.WriteLine($"Tests: {passed} passed, {failures} failed");
return failures == 0 ? 0 : 1;

sealed class Samples : ISampleProvider
{
    private readonly float[] samples;
    private readonly int maxRead;
    private int position;

    public Samples(float[] samples, int rate, int channels, int maxRead)
    {
        this.samples = samples;
        this.maxRead = maxRead;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int remaining = samples.Length - position;
        int read = Math.Min(Math.Min(count, maxRead), remaining);
        Array.Copy(samples, position, buffer, offset, read);
        position += read;
        return read;
    }
}
