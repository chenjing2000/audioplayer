using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace AudioPlayer;

internal sealed class FfmpegUnavailableException : IOException
{
    public FfmpegUnavailableException() : base("FFmpeg 未安装或无法运行，无法裁剪 M4A。请查看程序目录下的 FFmpeg-Setup.txt，按照说明安装。") { }
}

internal static class FfmpegM4aTrimmer
{
    internal static string ExecutablePath => Path.Combine(AppContext.BaseDirectory,
        "runtimes", "ffmpeg", "win-x64", "bin", "ffmpeg.exe");

    internal static string BuildConcat(string source, IReadOnlyList<SilenceCut> cuts,
        int sampleRate, double durationSeconds)
    {
        if (sampleRate <= 0 || !double.IsFinite(durationSeconds) || durationSeconds <= 0)
            throw new InvalidDataException("音频时长或采样率无效。");
        if (source.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            throw new InvalidDataException("音频文件名包含不支持的换行字符。");

        string escaped = Path.GetFullPath(source).Replace('\\', '/').Replace("'", "'\\''");
        var text = new StringBuilder("ffconcat version 1.0\n");
        double start = 0;
        foreach (var cut in cuts)
        {
            double end = cut.CutStart / (double)sampleRate;
            AppendInterval(text, escaped, start, end);
            start = cut.CutEnd / (double)sampleRate;
        }
        AppendInterval(text, escaped, start, durationSeconds);
        return text.ToString();
    }

    private static void AppendInterval(StringBuilder text, string source, double start, double end)
    {
        if (end <= start) return;
        text.Append("file '").Append(source).Append("'\n");
        text.Append("inpoint ").Append(start.ToString("0.#########", CultureInfo.InvariantCulture)).Append('\n');
        text.Append("outpoint ").Append(end.ToString("0.#########", CultureInfo.InvariantCulture)).Append('\n');
    }

    internal static void Trim(string source, string output, IReadOnlyList<SilenceCut> cuts,
        int sampleRate, double durationSeconds, CancellationToken token)
    {
        string executable = ExecutablePath;
        if (!File.Exists(executable)) throw new FfmpegUnavailableException();

        // Probe the executable lazily: broken shared-library deployments also fail here.
        if (Run(executable, ["-hide_banner", "-version"], token) != 0)
            throw new FfmpegUnavailableException();

        // ADTS supports AAC, not ALAC. Reject other M4A codecs instead of transcoding.
        if (Run(executable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-i", source,
                "-map", "0:a:0", "-t", "0.3", "-c:a", "copy", "-f", "adts", "-"],
                token) != 0)
            throw new NotSupportedException("仅支持 AAC 编码的 M4A 无重新编码裁剪。");

        string manifest = Path.Combine(Path.GetDirectoryName(output)!,
            "." + Guid.NewGuid().ToString("N") + ".ffconcat");
        try
        {
            File.WriteAllText(manifest, BuildConcat(source, cuts, sampleRate, durationSeconds),
                new UTF8Encoding(false));
            int code = Run(executable, ["-hide_banner", "-loglevel", "error", "-nostdin",
                "-f", "concat", "-safe", "0", "-protocol_whitelist", "file",
                "-i", manifest, "-map", "0:a:0", "-c:a", "copy", "-movflags", "+faststart",
                "-y", output], token);
            if (code != 0) throw new InvalidDataException("FFmpeg 拼接失败，未保存裁剪音频。");

            // Confirm the whole result can be decoded; a successful remux is not sufficient.
            if (Run(executable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-i",
                    output, "-map", "0:a:0", "-f", "null", "-"], token) != 0)
                throw new InvalidDataException("FFmpeg 输出无法完整解码。");
        }
        finally
        {
            if (File.Exists(manifest)) File.Delete(manifest);
        }
    }

    private static int Run(string executable, IReadOnlyList<string> arguments,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);

        try
        {
            if (!process.Start()) throw new FfmpegUnavailableException();
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            throw new FfmpegUnavailableException();
        }

        using var registration = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
        });
        Task stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
        token.ThrowIfCancellationRequested();
        return process.ExitCode;
    }
}
