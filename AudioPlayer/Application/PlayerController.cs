using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using AudioPlayer.Library;
using AudioPlayer.Playback;
using AudioPlayer.Settings;
using AudioPlayer.Silence;

namespace AudioPlayer.Application;

// Application-level orchestration; no controls, windows or reader dependencies.
internal sealed class PlayerController
{
    private readonly Dispatcher dispatcher;
    private readonly SettingsStore settingsStore;
    private readonly Queue<(bool Backward, bool Timed)> navigationRequests = new();
    private bool navigating;
    private bool completionQueued;
    private AudioTrack? selectedTrack;

    public PlayerController(Dispatcher dispatcher, SettingsStore settingsStore, PlaylistStore playlistStore)
    {
        this.dispatcher = dispatcher;
        this.settingsStore = settingsStore;
        Playlist = new Playlist(playlistStore);
        Session = new PlaybackSession(dispatcher);
        Session.NaturalPlaybackEnded += QueueNaturalCompletion;
        Session.CommandFinished += QueueNaturalCompletion;
        Silence.AnalysisFinished += RefreshSelectedCache;
    }

    public PlaybackSession Session { get; }
    public Playlist Playlist { get; }
    public SilenceTasks Silence { get; } = new();
    public AppSettings Settings { get; private set; } = new();
    public bool SettingsFileReady { get; private set; }
    public Task NavigationTask { get; private set; } = Task.CompletedTask;
    public event Action? FolderLoaded;
    public event Action? FolderStateChanged;
    public event Action<AudioTrack>? TrackAdvanced;
    public event Action<string>? Error;

    private static bool IsSettingsFileError(Exception exception)
        => exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException;

    public void InitializeSettings()
    {
        try { Settings = settingsStore.LoadOrCreate(); SettingsFileReady = true; }
        catch (Exception ex) when (IsSettingsFileError(ex))
        {
            Error?.Invoke($"配置读取或初始化失败，暂用默认参数：{ex.Message}");
        }
    }

    public Task RestoreAsync() => Session.RunActionAsync(async () =>
    {
        if (await Playlist.RestoreAsync(Settings.GetExcludedFolderNames()) && !Session.IsClosing)
            FolderLoaded?.Invoke();
    });

    public Task OpenFolderAsync(string path) => Session.RunActionAsync(() => LoadFolderAsync(path));

    internal async Task LoadFolderAsync(string path)
    {
        Silence.CancelAnalysis();
        Silence.CancelSelectionCheck();
        await Session.CloseTrackAsync();
        Task<bool> load = Playlist.LoadFolderAsync(path, Settings.GetExcludedFolderNames());
        // A collection reset and its selection notification precede completion.
        FolderStateChanged?.Invoke();
        if (await load && !Session.IsClosing) FolderLoaded?.Invoke();
    }

    public Task PlayAsync(AudioTrack track) => Session.RunActionAsync(() => PlayTrackAsync(track));

    internal async Task PlayTrackAsync(AudioTrack track)
    {
        if (!ReferenceEquals(track, Session.LoadedTrack))
        {
            await LoadTrackAsync(track);
            return;
        }
        if (!Session.IsPlaying) await Session.PlayLoadedAsync();
    }

    private async Task LoadTrackAsync(AudioTrack track)
    {
        Silence.CancelAnalysis();
        await Session.LoadTrackAsync(track);
        if (!Session.IsClosing && !Silence.IsTrimming) StartAnalysis(track);
    }

    private void StartAnalysis(AudioTrack track)
        => Silence.StartAnalysis(track.FilePath, Settings.GetSilenceOptions());

    public void SelectTrack(AudioTrack? track)
    {
        selectedTrack = track;
        RefreshSelectedCache();
    }

    private void RefreshSelectedCache()
        => Silence.CheckTrimAvailability(selectedTrack?.FilePath, Settings.GetSilenceOptions());

    public void AddTrimmedOutput(string path)
        => Playlist.AddFile(path, Settings.GetExcludedFolderNames());

    public async Task TrimSelectedAsync(Action<SilenceTrimResult> report)
    {
        if (Silence.IsTrimming || Silence.IsAnalyzing || Session.IsClosing || !Silence.CanTrimSelected
            || selectedTrack is not AudioTrack track) return;
        try
        {
            await Silence.TrimAsync(track.FilePath, Settings.GetSilenceOptions(), async result =>
            {
                if (Session.ActiveOperation is Task operation) await operation;
                if (Session.IsClosing) return;
                AddTrimmedOutput(result.OutputPath);
                report(result);
            });
        }
        finally
        {
            if (!Session.IsClosing)
            {
                if (Session.LoadedTrack is AudioTrack loaded && !Silence.AnalysisReady && !Silence.IsAnalyzing)
                    StartAnalysis(loaded);
                RefreshSelectedCache();
            }
        }
    }

    public Task ToggleAsync() => Session.RunActionAsync(async () =>
    {
        if (Session.IsLoaded) await Session.ToggleLoadedAsync();
        else if ((selectedTrack ?? Playlist.Tracks.FirstOrDefault()) is AudioTrack track)
            await LoadTrackAsync(track);
    }, ReaderAction.TogglePlayback);

    public bool CyclePlaybackMode()
    {
        PlaybackMode previous = Settings.PlaybackMode;
        Settings.PlaybackMode = (PlaybackMode)(((int)previous + 1) % 4);
        try { settingsStore.Save(Settings); }
        catch (Exception ex) when (IsSettingsFileError(ex))
        {
            Settings.PlaybackMode = previous;
            Error?.Invoke($"配置保存失败，播放模式未更改：{ex.Message}");
            return false;
        }
        return true;
    }

    public bool ApplySettings(AppSettings settings)
    {
        try { settingsStore.Save(settings); }
        catch (Exception ex) when (IsSettingsFileError(ex))
        {
            Error?.Invoke($"配置保存失败，参数未更改：{ex.Message}");
            return false;
        }
        bool detectionChanged = Settings.FrameMilliseconds != settings.FrameMilliseconds
            || Settings.MinimumSilenceMilliseconds != settings.MinimumSilenceMilliseconds
            || Settings.SilenceThresholdDb != settings.SilenceThresholdDb;
        Settings = settings;
        if (detectionChanged && Session.LoadedTrack is AudioTrack track)
        {
            if (Silence.IsTrimming) Silence.CancelAnalysis();
            else StartAnalysis(track);
        }
        RefreshSelectedCache();
        return true;
    }

    public void PreviewScriptSeek(double seconds, bool readerVisible)
    {
        if (Session.IsClosing || Session.IsBusy || Session.IsDragging || navigating) return;
        if (Session.ModalDialogOpen || !Session.IsLoaded || Session.LastError is not null ||
            Session.NaturalCompletion is not null || !readerVisible)
        {
            _ = Session.CancelPreviewAsync();
            return;
        }
        Session.BeginPreview(seconds);
    }

    public Task NavigateAsync(bool backward, bool timed)
    {
        if (Session.ModalDialogOpen || Session.IsClosing || Session.IsDragging || !Session.IsLoaded
            || (Session.IsBusy && !navigating) || (!timed && (Silence.IsAnalyzing || !Silence.AnalysisReady)))
            return Task.CompletedTask;
        if (timed && (backward ? Settings.BackwardStepSeconds : Settings.ForwardStepSeconds) == 0)
            return Task.CompletedTask;
        navigationRequests.Enqueue((backward, timed));
        if (!navigating)
        {
            navigating = true;
            NavigationTask = DrainNavigationAsync();
        }
        return NavigationTask;
    }

    private async Task DrainNavigationAsync()
    {
        try
        {
            while (navigationRequests.Count > 0 && !Session.IsClosing)
            {
                var (backward, timed) = navigationRequests.Dequeue();
                await Session.RunActionAsync(async () =>
                {
                    TimeSpan current = Session.Position;
                    TimeSpan? target = timed
                        ? current + TimeSpan.FromSeconds(backward ? -Settings.BackwardStepSeconds : Settings.ForwardStepSeconds)
                        : backward
                        ? Silence.FindPrevious(current, Settings.NavigationToleranceMilliseconds)
                        : Silence.FindNext(current, Settings.NavigationToleranceMilliseconds);
                    // An actual user seek supersedes any pending natural continuation.
                    if (target.HasValue) await Session.SeekLoadedAsync(target.Value);
                }, ReaderAction.PreservePlayback);
            }
        }
        finally { navigationRequests.Clear(); navigating = false; QueueNaturalCompletion(); }
    }

    internal static int NextPlaybackIndex(PlaybackMode mode, int index, int count)
    {
        if (index < 0 || index >= count) return -1;
        return mode switch
        {
            PlaybackMode.RepeatOne => index,
            PlaybackMode.Sequential when index + 1 < count => index + 1,
            PlaybackMode.RepeatAll => (index + 1) % count,
            _ => -1
        };
    }

    private bool CanContinuePlayback()
        => !Session.IsClosing && !Session.IsBusy && !Session.IsDragging && !navigating && !Session.ModalDialogOpen &&
           Session.PreviewPosition is null;

    internal void QueueNaturalCompletion()
    {
        if (!CanContinuePlayback() || completionQueued || Session.NaturalCompletion is null) return;
        completionQueued = true;
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            completionQueued = false;
            if (!CanContinuePlayback() || Session.NaturalCompletion is not object token) return;
            await Session.RunActionAsync(async () =>
            {
                if (!Session.ConsumeNaturalCompletion(token) || Session.LoadedTrack is null) return;
                int index = NextPlaybackIndex(Settings.PlaybackMode, Playlist.Tracks.IndexOf(Session.LoadedTrack), Playlist.Tracks.Count);
                if (index < 0) return;
                AudioTrack next = Playlist.Tracks[index];
                if (ReferenceEquals(next, Session.LoadedTrack))
                {
                    await Session.PlayLoadedAsync();
                }
                else
                {
                    await LoadTrackAsync(next);
                    if (!Session.IsClosing) TrackAdvanced?.Invoke(next);
                }
            });
        }));
    }

    public async Task ShutdownAsync()
    {
        Playlist.CancelPending();
        Silence.CancelPending();
        try
        {
            if (Session.ActiveOperation is not null) await Session.ActiveOperation;
            await NavigationTask;
            await Silence.WaitForPendingAsync();
            await Session.CloseTrackAsync();
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
        finally
        {
            Session.Dispose();
            Playlist.Dispose();
        }
    }
}
