using System.Windows.Threading;
using AudioPlayer.Library;

namespace AudioPlayer.Playback;

internal enum ReaderAction { DiscardPreview, PreservePlayback, TogglePlayback, CommitPreview }

// One owning dispatcher for session state; the engine owns its device lock.
internal sealed class PlaybackSession : IDisposable
{
    private readonly PlaybackEngine player = new();
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer scriptSeekTimer;
    private sealed record ScriptSeek(double Target, bool Resume);
    private ScriptSeek? scriptSeek;
    private Exception? displayedPlaybackError;

    public PlaybackSession(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        scriptSeekTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            { Interval = TimeSpan.FromMilliseconds(150) };
        scriptSeekTimer.Tick += async (_, _) => await CommitPreviewAsync();
        player.StateChanged += Player_StateChanged;
        player.NaturalPlaybackEnded += Player_NaturalPlaybackEnded;
    }

    public AudioTrack? LoadedTrack { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsClosing { get; private set; }
    public bool IsDragging { get; private set; }
    public bool ModalDialogOpen { get; set; }
    public Task? ActiveOperation { get; private set; }
    public bool IsLoaded => player.IsLoaded;
    public bool IsPlaying => player.IsPlaying;
    public bool IsPaused => player.IsPaused;
    public bool PlayRequested => player.IsPlaying || scriptSeek?.Resume == true;
    public double? PreviewPosition => scriptSeek?.Target;
    public bool PreviewTimerEnabled => scriptSeekTimer.IsEnabled;
    public TimeSpan PreviewDebounce => scriptSeekTimer.Interval;
    public TimeSpan Position => player.GetCurrentTime();
    public TimeSpan Duration => player.GetDuration();
    public Exception? LastError => player.LastError;
    public object? NaturalCompletion => player.NaturalCompletion;
    public bool ConsumeNaturalCompletion(object token) => player.ConsumeNaturalCompletion(token);
    public bool ResetReader => !IsLoaded || (!IsPlaying && !IsPaused &&
        (Position <= TimeSpan.Zero || Position >= Duration));

    public event Action? StateChanged;
    public event Action? NaturalPlaybackEnded;
    public event Action? CommandFinished;
    public event Action<double?>? Reconcile;
    public event Action<double>? PreviewChanged;
    public event Action<string>? Error;

    private void Player_NaturalPlaybackEnded(object? sender, EventArgs e)
    {
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => Player_NaturalPlaybackEnded(sender, e));
            return;
        }
        if (!IsClosing) NaturalPlaybackEnded?.Invoke();
    }

    internal async Task LoadTrackAsync(AudioTrack track)
    {
        LoadedTrack = null;
        await player.OpenAsync(track.FilePath);
        if (IsClosing) return;
        if (Duration <= TimeSpan.Zero)
        {
            await player.CloseAsync();
            throw new InvalidOperationException("音频文件没有可播放的内容：" + track.DisplayName);
        }
        LoadedTrack = track;
        try { await player.PlayAsync(); }
        catch { LoadedTrack = null; await player.CloseAsync(); throw; }
    }

    internal async Task CloseTrackAsync()
    {
        await player.CloseAsync();
        LoadedTrack = null;
    }

    internal Task PlayLoadedAsync() => player.PlayAsync();
    internal Task ToggleLoadedAsync()
    {
        if (player.IsPlaying) { player.Pause(); return Task.CompletedTask; }
        return player.PlayAsync();
    }

    internal Task SeekLoadedAsync(TimeSpan position) => player.SeekAsync(position);
    public Task StopAsync() => RunActionAsync(player.StopAsync);
    public Task SeekAsync(TimeSpan position) => RunActionAsync(() => player.SeekAsync(position), ReaderAction.PreservePlayback);

    public bool BeginDrag()
    {
        if (IsBusy || IsClosing || !IsLoaded) return false;
        scriptSeekTimer.Stop();
        IsDragging = true;
        return true;
    }

    public bool EndDrag()
    {
        if (!IsDragging) return false;
        IsDragging = false;
        return true;
    }

    internal async Task RunActionAsync(Func<Task> action, ReaderAction readerAction = ReaderAction.DiscardPreview)
    {
        if (IsBusy || IsClosing) return;
        IsBusy = true;
        var preview = TakePreview();
        bool resume = preview?.Resume == true;
        bool committed = false;
        StateChanged?.Invoke();

        async Task ExecuteAsync()
        {
            try
            {
                // During a preview the audio is already paused. A toggle from
                // the prior playing state must therefore leave it paused.
                if (!(readerAction == ReaderAction.TogglePlayback && resume))
                    await action();
                if (resume && (readerAction is ReaderAction.PreservePlayback or ReaderAction.CommitPreview) &&
                    !IsClosing && player.LastError is null && player.IsLoaded &&
                    player.IsPaused && player.GetCurrentTime() < player.GetDuration())
                    await player.PlayAsync();
                committed = readerAction == ReaderAction.CommitPreview && player.LastError is null;
            }
            catch (Exception ex)
            {
                if (!IsClosing) Error?.Invoke(ex.Message);
            }
        }

        Task work = ExecuteAsync();
        ActiveOperation = work;
        try { await work; }
        finally
        {
            ActiveOperation = null;
            IsBusy = false;
            if (!IsClosing)
            {
                // Every completed host command publishes actual playback state,
                // invalidating old reader requests even when position is unchanged.
                Reconcile?.Invoke(committed ? preview?.Target : null);
                StateChanged?.Invoke();
                CommandFinished?.Invoke();
            }
        }
    }

    private void Player_StateChanged(object? sender, EventArgs e)
    {
        if (!dispatcher.CheckAccess()) { dispatcher.BeginInvoke(() => Player_StateChanged(sender, e)); return; }
        if (IsClosing) return;
        if (player.LastError is null) displayedPlaybackError = null;
        else if (!ReferenceEquals(player.LastError, displayedPlaybackError))
        {
            displayedPlaybackError = player.LastError;
            TakePreview();
            if (!IsBusy) Reconcile?.Invoke(null);
            Error?.Invoke("播放设备异常：" + player.LastError.Message);
        }
        StateChanged?.Invoke();
    }

    internal void BeginPreview(double seconds)
    {
        if (IsClosing || IsBusy || !double.IsFinite(seconds) || seconds < 0 ||
            !player.IsLoaded || player.LastError is not null || player.NaturalCompletion is not null) return;
        double target = Math.Clamp(seconds, 0, player.GetDuration().TotalSeconds);
        bool first = scriptSeek is null;
        var preview = new ScriptSeek(target, scriptSeek?.Resume ?? player.IsPlaying);
        scriptSeek = preview; // Publish before Pause, which raises StateChanged synchronously.
        if (first && preview.Resume)
        {
            try { player.Pause(); }
            catch (Exception ex)
            {
                TakePreview();
                Reconcile?.Invoke(null);
                StateChanged?.Invoke();
                Error?.Invoke(ex.Message);
                return;
            }
        }
        // A device error or natural completion may have canceled the preview
        // during Pause; never recreate it from the old local value.
        if (!ReferenceEquals(scriptSeek, preview)) return;
        if (player.LastError is not null || player.NaturalCompletion is not null)
        {
            TakePreview();
            Reconcile?.Invoke(null);
            StateChanged?.Invoke();
            CommandFinished?.Invoke();
            return;
        }
        PreviewChanged?.Invoke(target);
        scriptSeekTimer.Stop();
        scriptSeekTimer.Start();
    }

    internal async Task CommitPreviewAsync()
    {
        scriptSeekTimer.Stop();
        if (scriptSeek is not { } preview) return;
        if (IsClosing || IsBusy || ModalDialogOpen || IsDragging || !player.IsLoaded || player.LastError is not null)
        {
            await CancelPreviewAsync();
            return;
        }
        await RunActionAsync(() => player.SeekAsync(TimeSpan.FromSeconds(preview.Target)), ReaderAction.CommitPreview);
    }

    private ScriptSeek? TakePreview()
    {
        scriptSeekTimer.Stop();
        var preview = scriptSeek;
        scriptSeek = null;
        return preview;
    }

    internal async Task CancelPreviewAsync()
    {
        if (IsClosing || IsBusy || IsDragging) return;
        var preview = TakePreview();
        if (preview?.Resume == true && player.LastError is null && player.IsPaused)
            await RunActionAsync(player.PlayAsync);
        else
        {
            Reconcile?.Invoke(null);
            StateChanged?.Invoke();
            CommandFinished?.Invoke();
        }
    }

    public void BeginClosing()
    {
        IsClosing = true;
        TakePreview();
    }

    public void Dispose()
    {
        scriptSeekTimer.Stop();
        player.StateChanged -= Player_StateChanged;
        player.NaturalPlaybackEnded -= Player_NaturalPlaybackEnded;
        player.Dispose();
    }
}
