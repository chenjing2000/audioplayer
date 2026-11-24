using System.IO;

namespace AudioPlayer;

public static class AudioLibrary
{
    public static List<AudioTrack> LoadFolder(string folderPath, CancellationToken cancellation = default,
        IReadOnlySet<string>? excludedNames = null)
    {
        string root = Path.GetFullPath(folderPath);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        cancellation.ThrowIfCancellationRequested();
        return LoadPaths(root, EnumerateMediaPaths(root, options, cancellation, excludedNames), cancellation);
    }

    private static IEnumerable<string> EnumerateMediaPaths(string root, EnumerationOptions options,
        CancellationToken cancellation, IReadOnlySet<string>? excludedNames)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            string folder = pending.Pop();
            foreach (string child in Directory.EnumerateDirectories(folder, "*", options))
            {
                cancellation.ThrowIfCancellationRequested();
                if (excludedNames?.Contains(Path.GetFileName(child)) != true) pending.Push(child);
            }
            foreach (string file in Directory.EnumerateFiles(folder, "*", options))
            {
                cancellation.ThrowIfCancellationRequested();
                yield return file;
            }
        }
    }

    internal static bool IsPlaylistPath(string root, string path, IReadOnlySet<string>? excludedNames = null)
    {
        if (!IsWithinFolder(root, path) || !IsSupportedFile(path) || IsTrimTemporaryFile(path))
            return false;
        if (excludedNames is null || excludedNames.Count == 0) return true;
        string[] parts = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
        return !parts.Take(parts.Length - 1).Any(excludedNames.Contains);
    }

    internal static List<AudioTrack> LoadPaths(string root, IEnumerable<string> paths,
        CancellationToken cancellation = default, bool restore = false, IReadOnlySet<string>? excludedNames = null)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string candidate in paths)
        {
            cancellation.ThrowIfCancellationRequested();
            string path = Path.GetFullPath(candidate);
            if (IsPlaylistPath(root, path, excludedNames) && (!restore || IsRestorablePath(root, path, cancellation)))
                files.Add(path);
        }
        cancellation.ThrowIfCancellationRequested();
        var sorted = files.ToArray();
        Array.Sort(sorted, (left, right) => ComparePaths(root, left, right));
        var tracks = new List<AudioTrack>(sorted.Length);
        foreach (string path in sorted)
        {
            cancellation.ThrowIfCancellationRequested();
            tracks.Add(ReadTrack(path));
        }
        cancellation.ThrowIfCancellationRequested();
        return tracks;
    }

    // Attribute reads distinguish missing paths from access and IO errors.
    private static bool IsRestorablePath(string root, string path, CancellationToken cancellation)
    {
        string current = root;
        string[] parts = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
        try
        {
            for (int i = 0; i < parts.Length; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                current = Path.Combine(current, parts[i]);
                FileAttributes attributes = File.GetAttributes(current);
                bool directory = (attributes & FileAttributes.Directory) != 0;
                if ((attributes & FileAttributes.ReparsePoint) != 0 || directory != (i < parts.Length - 1))
                    return false;
            }
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    internal static bool IsSupportedFile(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".wav" or ".mp3" or ".m4a";

    internal static bool IsWithinFolder(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != "." && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    internal static int ComparePaths(string root, string left, string right)
        => CompareNames(Path.GetRelativePath(root, left), Path.GetRelativePath(root, right));

    // Only hide our own in-progress trim output; keep other dot-prefixed audio visible.
    internal static bool IsTrimTemporaryFile(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        if (!stem.StartsWith(".", StringComparison.Ordinal)) return false;
        int marker = stem.LastIndexOf(".new.", StringComparison.Ordinal);
        return marker > 1 && Guid.TryParseExact(stem[(marker + 5)..], "N", out _);
    }

    internal static AudioTrack ReadTrack(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            return new AudioTrack
            {
                FilePath = Path.GetFullPath(path),
                Album = file.Tag.Album ?? "",
                Duration = file.Properties.Duration
            };
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is TagLib.CorruptFileException || ex is TagLib.UnsupportedFormatException)
        {
            return new AudioTrack { FilePath = Path.GetFullPath(path), LoadError = ex.Message };
        }
    }

    internal static int CompareNames(string left, string right)
    {
        int a = 0, b = 0;
        while (a < left.Length && b < right.Length)
        {
            if (char.IsAsciiDigit(left[a]) && char.IsAsciiDigit(right[b]))
            {
                int aEnd = a;
                int bEnd = b;
                while (aEnd < left.Length && char.IsAsciiDigit(left[aEnd])) aEnd++;
                while (bEnd < right.Length && char.IsAsciiDigit(right[bEnd])) bEnd++;
                int aNumber = a;
                int bNumber = b;
                while (aNumber < aEnd - 1 && left[aNumber] == '0') aNumber++;
                while (bNumber < bEnd - 1 && right[bNumber] == '0') bNumber++;
                int lengthOrder = (aEnd - aNumber).CompareTo(bEnd - bNumber);
                if (lengthOrder != 0) return lengthOrder;
                for (int i = 0; i < aEnd - aNumber; i++)
                {
                    int digitOrder = left[aNumber + i].CompareTo(right[bNumber + i]);
                    if (digitOrder != 0) return digitOrder;
                }
                a = aEnd;
                b = bEnd;
            }
            else
            {
                int order = char.ToUpperInvariant(left[a]).CompareTo(char.ToUpperInvariant(right[b]));
                if (order != 0) return order;
                a++;
                b++;
            }
        }
        int remainingOrder = (left.Length - a).CompareTo(right.Length - b);
        return remainingOrder != 0 ? remainingOrder : StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }
}
