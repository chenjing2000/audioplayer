using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace AudioPlayer.Library;

// All collection changes run on the caller's owning context. File discovery
// and restoration run off that context; cancellation prevents a late commit.
internal sealed class Playlist : IDisposable
{
    private readonly PlaylistStore store;
    private readonly ObservableCollection<AudioTrack> tracks = new();
    private readonly CancellationTokenSource cancellation = new();

    public Playlist(PlaylistStore store)
    {
        this.store = store;
        Tracks = new ReadOnlyObservableCollection<AudioTrack>(tracks);
    }

    public string? RootFolder { get; private set; }
    public ReadOnlyObservableCollection<AudioTrack> Tracks { get; }
    public event Action<string>? Error;

    public async Task<bool> RestoreAsync(IReadOnlySet<string> excludedNames)
    {
        CancellationToken token = cancellation.Token;
        PlaylistSnapshot? snapshot = await Task.Run(store.Load, token);
        if (snapshot is null || token.IsCancellationRequested || snapshot.RootFolder is null) return false;
        string root = Path.GetFullPath(snapshot.RootFolder);
        List<AudioTrack>? files;
        try
        {
            files = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if ((File.GetAttributes(root) & FileAttributes.Directory) == 0) return null;
                }
                catch (FileNotFoundException) { return null; }
                catch (DirectoryNotFoundException) { return null; }
                return AudioLibrary.LoadPaths(root, snapshot.Files, token, restore: true, excludedNames: excludedNames);
            }, token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!token.IsCancellationRequested)
                Error?.Invoke("恢复播放列表失败，已保留磁盘上的列表。请检查目录访问权限后重启：" + ex.Message);
            return false;
        }
        if (token.IsCancellationRequested) return false;
        if (files is null)
        {
            RootFolder = null;
            tracks.Clear();
            Save();
            Error?.Invoke("上次的音频文件夹不存在，已清空播放列表。");
            return false;
        }
        RootFolder = root;
        tracks.Clear();
        foreach (var file in files) tracks.Add(file);
        if (!snapshot.Files.SequenceEqual(files.Select(file => file.FilePath), StringComparer.Ordinal))
            Save();
        return true;
    }

    public async Task<bool> LoadFolderAsync(string path, IReadOnlySet<string> excludedNames)
    {
        CancellationToken token = cancellation.Token;
        RootFolder = null;
        tracks.Clear();
        List<AudioTrack> files;
        try { files = await Task.Run(() => AudioLibrary.LoadFolder(path, token, excludedNames), token); }
        catch
        {
            // An unfinished scan canceled on exit must not erase the last saved list.
            if (!token.IsCancellationRequested) Save();
            throw;
        }
        if (token.IsCancellationRequested) return false;
        RootFolder = Path.GetFullPath(path);
        foreach (var file in files) tracks.Add(file);
        Save();
        return true;
    }

    public void AddFile(string path, IReadOnlySet<string> excludedNames)
    {
        if (RootFolder is not string root || !AudioLibrary.IsPlaylistPath(root, path, excludedNames)
            || tracks.Any(track => string.Equals(track.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            return;
        var added = AudioLibrary.ReadTrack(path);
        int index = 0;
        while (index < tracks.Count && AudioLibrary.ComparePaths(root, tracks[index].FilePath, path) < 0)
            index++;
        tracks.Insert(index, added);
        Save();
    }

    private void Save()
    {
        try { store.Save(RootFolder, tracks.Select(track => track.FilePath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            if (!cancellation.IsCancellationRequested)
                Error?.Invoke("播放列表保存失败，磁盘上可能仍是旧列表。请刷新或重新打开文件夹重试：" + ex.Message);
        }
    }

    // The application awaits the active command before disposing this module.
    public void CancelPending() => cancellation.Cancel();
    public void Dispose() => cancellation.Dispose();
}
