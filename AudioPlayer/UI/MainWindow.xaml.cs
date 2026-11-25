using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

using AudioPlayer.Playback;
using AudioPlayer.Library;
using AudioPlayer.Silence;
using AudioPlayer.Script;
using AudioPlayer.Settings;

namespace AudioPlayer.UI;

public partial class MainWindow : Window
{
    private readonly PlaybackEngine player = new();
    internal Playlist Playlist { get; }
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(NormalProgressUpdateMs) };
    private AppSettings appSettings = new();
    private readonly SettingsStore settingsStore;
    private bool settingsFileReady;
    internal SilenceTasks Silence { get; } = new();
    private AudioTrack? loadedTrack;
    private AudioTrack? indicatedTrack;
    private bool operationBusy;
    private ToastHost.ToastMessage? analysisToast;
    private bool dragging;
    private bool closing;
    private bool allowClose;
    private Task? activeOperation;
    private readonly Queue<(Key Key, bool Timed)> navigationKeys = new();
    private bool navigating;
    private Task navigationTask = Task.CompletedTask;
    private Exception? displayedPlaybackError;
    private bool modalDialogOpen;
    private bool completionQueued;
    internal ScriptWindowHost ScriptHost { get; }
    private const int ScriptSeekDebounceMs = 150;
    private const int ReaderPlaybackUpdateMs = 25;
    private const int NormalProgressUpdateMs = 100;
    private readonly DispatcherTimer scriptSeekTimer = new() { Interval = TimeSpan.FromMilliseconds(ScriptSeekDebounceMs) };
    // A preview is one transaction: target + playback intent. Once consumed,
    // the existing operationBusy gate owns the asynchronous audio operation.
    private sealed record ScriptSeek(double Target, bool Resume);
    private ScriptSeek? scriptSeek;
    private enum ReaderAction { DiscardPreview, PreservePlayback, TogglePlayback, CommitPreview }

    public MainWindow() : this(new SettingsStore(),
        new PlaylistStore(PlaylistStore.GetFilePath(SettingsStore.GetExecutablePath())))
    {
    }

    internal MainWindow(SettingsStore settingsStore, PlaylistStore? playlistStore = null,
        Func<ScriptWindow>? scriptWindowFactory = null)
    {
        this.settingsStore = settingsStore;
        // Test settings paths keep their playlists isolated from the real executable.
        Playlist = new Playlist(playlistStore ?? new PlaylistStore(
            settingsStore.FilePath[..^".settings.json".Length] + ".playlist.json"));
        ScriptHost = new ScriptWindowHost(this, scriptWindowFactory);
        InitializeComponent();
        Rect workArea = SystemParameters.WorkArea;
        double desiredWidth = Math.Round(workArea.Width * 0.5);
        double desiredHeight = Math.Round(workArea.Height * 0.6);
        Width = Math.Max(desiredWidth, MinWidth);
        Height = Math.Max(desiredHeight, MinHeight);
        Left = Math.Round(workArea.Left + (workArea.Width - Width) / 2);
        Top = Math.Round(workArea.Top + (workArea.Height - Height) / 2);
        ProgressSlider.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(ProgressSlider_MouseDown), true);
        AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(ProgressSlider_MouseUp), true);
        TrackList.ItemsSource = Playlist.Tracks;
        Playlist.Error += message => { if (!closing) ToastMessages.Show(message, ToastLevel.Error); };
        Silence.StateChanged += UpdateControls;
        Silence.AnalysisCanceled += CancelAnalysisToast;
        Silence.AnalysisStarted += path => analysisToast = ToastMessages.ShowTask(
            $"正在检测 {Path.GetFileName(path)} 的停顿，请等待检测完成。");
        Silence.AnalysisCompleted += ReportAnalysis;
        Silence.AnalysisFailed += ReportAnalysisFailure;
        Silence.AnalysisFinished += RefreshSelectedCache;
        player.StateChanged += Player_StateChanged;
        player.NaturalPlaybackEnded += Player_NaturalPlaybackEnded;
        timer.Tick += (_, _) => RefreshProgress();
        timer.Start();
        scriptSeekTimer.Tick += async (_, _) => await CommitScriptSeekAsync();
        try
        {
            appSettings = settingsStore.LoadOrCreate();
            settingsFileReady = true;
        }
        catch (Exception ex) when (IsSettingsFileError(ex))
        {
            ToastMessages.Show($"配置读取或初始化失败，暂用默认参数：{ex.Message}", ToastLevel.Error);
        }
        UpdatePlaybackModeButton();
        PlaybackModeButton.IsEnabled = settingsFileReady;
        Loaded += RestorePlaylistOnLoad;
        ScriptHost.VisibilityChanged += UpdateScriptButton;
        ScriptHost.NavigationPreview += PreviewScriptSeek;
        ScriptHost.NavigationCanceled += async () => await RestoreCanceledScriptSeekAsync();
        ScriptHost.Shortcut += async (key, modifiers) => await HandleShortcutAsync(key, modifiers);
    }

    private async void RestorePlaylistOnLoad(object sender, RoutedEventArgs e)
    {
        Loaded -= RestorePlaylistOnLoad;
        await RunActionAsync(RestorePlaylistAsync);
    }

    private async Task RestorePlaylistAsync()
    {
        if (await Playlist.RestoreAsync(appSettings.GetExcludedFolderNames()) && !closing)
            ReportFolderLoad();
    }

    private static bool IsSettingsFileError(Exception exception)
        => exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException;

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (operationBusy || closing || modalDialogOpen) return;
        var dialog = new OpenFolderDialog { Title = "选择教材音频文件夹" };
        modalDialogOpen = true;
        try
        {
            await RestoreCanceledScriptSeekAsync();
            if (closing) return;
            if (dialog.ShowDialog(this) == true)
                await RunActionAsync(() => LoadFolderAsync(dialog.FolderName));
        }
        finally { modalDialogOpen = false; QueueNaturalCompletion(); }
    }

    internal async Task LoadFolderAsync(string path)
    {
        // Stop playback and old analysis before replacing the playlist.
        Silence.CancelAnalysis();
        Silence.CancelSelectionCheck();
        await player.CloseAsync();
        loadedTrack = null;
        Task<bool> load = Playlist.LoadFolderAsync(path, appSettings.GetExcludedFolderNames());
        RefreshProgress();
        UpdateControls();
        if (await load && !closing)
            ReportFolderLoad();
    }

    private void ReportFolderLoad()
    {
        int errors = Playlist.Tracks.Count(file => file.LoadError is not null);
        if (errors > 0) ToastMessages.Show($"{errors} 个文件的标签读取失败，悬停对应行可查看原因。", ToastLevel.Warning);
        else if (Playlist.Tracks.Count == 0) ToastMessages.Show("此文件夹中没有 WAV、MP3 或 M4A 音频文件。", ToastLevel.Warning);
        RefreshProgress();
        UpdateControls();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        string? folder = Playlist.RootFolder;
        if (folder is null || operationBusy || Silence.IsTrimming || closing) return;
        await RunActionAsync(() => LoadFolderAsync(folder));
    }

    private async void TrackList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (operationBusy || closing || e.ChangedButton != MouseButton.Left) return;
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source is not null && source is not DataGridRow)
            source = VisualTreeHelper.GetParent(source);
        if (source is DataGridRow row && row.Item is AudioTrack track)
            await RunActionAsync(() => PlayTrackAsync(track));
    }

    private void TrackRow_ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is DataGridRow { ToolTip: ToolTip tooltip })
            tooltip.MaxWidth = Math.Max(1, ActualWidth - 48);
    }

    private async void TrackRow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        if (sender is not DataGridRow row || !row.IsKeyboardFocusWithin) return;

        e.Handled = true;
        if (operationBusy || closing || row.Item is not AudioTrack track) return;
        await RunActionAsync(() => PlayTrackAsync(track));
    }

    private async Task PlayTrackAsync(AudioTrack track)
    {
        if (!ReferenceEquals(track, loadedTrack))
        {
            await LoadTrackAsync(track);
            return;
        }

        if (!player.IsPlaying)
            await player.PlayAsync();
    }

    private async Task LoadTrackAsync(AudioTrack track)
    {
        Silence.CancelAnalysis();
        // OpenAsync releases the previous reader even if the replacement cannot open.
        loadedTrack = null;
        await player.OpenAsync(track.FilePath);
        if (closing) return;
        if (player.GetDuration() <= TimeSpan.Zero)
        {
            await player.CloseAsync();
            throw new InvalidOperationException("音频文件没有可播放的内容：" + track.DisplayName);
        }
        loadedTrack = track;
        try { await player.PlayAsync(); }
        catch { loadedTrack = null; await player.CloseAsync(); throw; }
        if (!Silence.IsTrimming) StartAnalysis(track);
    }

    private void StartAnalysis(AudioTrack track)
        => Silence.StartAnalysis(track.FilePath, appSettings.GetSilenceOptions());

    private void CancelAnalysisToast()
    {
        if (analysisToast is not null) ToastMessages.CancelTask(analysisToast);
        analysisToast = null;
    }

    private void ReportAnalysis(SilenceAnalysisSummary result)
    {
        if (analysisToast is not { } toast) return;
        if (!result.Saved)
            ToastMessages.CompleteTask(toast, "检测完成，但无法保存静音检测结果；本次仍可导航，裁剪不可用。", ToastLevel.Warning);
        else
            ToastMessages.CompleteTask(toast,
                $"检测到 {result.RangeCount} 段停顿，可压缩 {result.CutCount} 段，预计减少 {result.RemovedSeconds:F1} 秒。",
                ToastLevel.Info);
        analysisToast = null;
    }

    private void ReportAnalysisFailure(Exception ex)
    {
        string message = "停顿检测失败，普通播放仍可用：" + ex.Message;
        if (analysisToast is { } toast) ToastMessages.CompleteTask(toast, message, ToastLevel.Error);
        else ToastMessages.Show(message, ToastLevel.Error);
        analysisToast = null;
    }

    private void TrackList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => RefreshSelectedCache();

    private void RefreshSelectedCache()
        => Silence.CheckTrimAvailability((TrackList.SelectedItem as AudioTrack)?.FilePath, appSettings.GetSilenceOptions());

    private async void TrimButton_Click(object sender, RoutedEventArgs e)
    {
        if (Silence.IsTrimming || Silence.IsAnalyzing || closing || !Silence.CanTrimSelected) return;
        if (TrackList.SelectedItem is not AudioTrack track) return;

        var toast = ToastMessages.ShowTask(
            $"正在压缩 {Path.GetFileName(track.FilePath)} 的长静音，请等待裁剪完成。");
        try
        {
            await Silence.TrimAsync(track.FilePath, appSettings.GetSilenceOptions(), async result =>
            {
                // A folder switch can overlap trimming; wait for its committed root.
                if (activeOperation is Task operation) await operation;
                if (closing) return;
                AddTrimmedOutput(result.OutputPath);
                if (!result.OutputSilenceSaved)
                    ToastMessages.CompleteTask(toast, "音频已保存，但停顿信息更新失败：" + result.OutputPath,
                        ToastLevel.Warning, TimeSpan.FromSeconds(15));
                else
                    ToastMessages.CompleteTask(toast,
                        $"裁剪完成：{result.OutputPath}。压缩 {result.TrimmedRangeCount} 段，减少 " +
                        $"{result.RemovedDuration.TotalSeconds:F3} 秒；原时长 " +
                        $"{AudioTrack.FormatTime(result.OriginalDuration)}，新时长 {AudioTrack.FormatTime(result.OutputDuration)}。",
                        ToastLevel.Info, TimeSpan.FromSeconds(15));
            });
        }
        catch (FfmpegUnavailableException ex)
        {
            if (!closing)
                ToastMessages.CompleteTask(toast, ex.Message, ToastLevel.Error, TimeSpan.FromSeconds(15));
        }
        catch (OperationCanceledException)
        {
            if (!closing)
                ToastMessages.CompleteTask(toast, "长静音裁剪已取消。", ToastLevel.Info);
        }
        catch (Exception ex)
        {
            if (!closing)
                ToastMessages.CompleteTask(toast, "裁剪失败：" + ex.Message, ToastLevel.Error);
        }
        finally
        {
            if (!closing)
            {
                if (loadedTrack is not null && !Silence.AnalysisReady && !Silence.IsAnalyzing)
                    StartAnalysis(loadedTrack);
                RefreshSelectedCache();
                UpdateControls();
            }
        }
    }

    internal void AddTrimmedOutput(string path)
        => Playlist.AddFile(path, appSettings.GetExcludedFolderNames());

    private async Task TogglePlaybackAsync()
    {
        if (player.IsLoaded)
        {
            if (player.IsPlaying) player.Pause();
            else await player.PlayAsync();
        }
        else
        {
            var track = TrackList.SelectedItem as AudioTrack ?? Playlist.Tracks.FirstOrDefault();
            if (track is not null) await LoadTrackAsync(track);
        }
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e) => await ToggleFromUserAsync();

    private Task ToggleFromUserAsync() => RunActionAsync(TogglePlaybackAsync, ReaderAction.TogglePlayback);
    private async void StopButton_Click(object sender, RoutedEventArgs e) => await RunActionAsync(player.StopAsync);

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

    private void UpdatePlaybackModeButton()
    {
        PlaybackMode mode = appSettings.PlaybackMode;
        string name = mode switch
        {
            PlaybackMode.RepeatOne => "单曲循环",
            PlaybackMode.Sequential => "顺序播放",
            PlaybackMode.RepeatAll => "列表循环",
            _ => "单曲播放"
        };
        PlaybackModeImage.Source = (ImageSource)FindResource(mode + "Icon");
        PlaybackModeButton.ToolTip = name;
        AutomationProperties.SetName(PlaybackModeButton, "播放模式：" + name);
    }

    private void PlaybackModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (operationBusy || closing || modalDialogOpen || !settingsFileReady) return;
        PlaybackMode previous = appSettings.PlaybackMode;
        appSettings.PlaybackMode = (PlaybackMode)(((int)previous + 1) % 4);
        try { settingsStore.Save(appSettings); }
        catch (Exception ex) when (IsSettingsFileError(ex))
        {
            appSettings.PlaybackMode = previous;
            ToastMessages.Show($"配置保存失败，播放模式未更改：{ex.Message}", ToastLevel.Error);
            return;
        }
        UpdatePlaybackModeButton();
    }

    private void Player_NaturalPlaybackEnded(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Player_NaturalPlaybackEnded(sender, e));
            return;
        }
        QueueNaturalCompletion();
    }

    private bool CanContinuePlayback()
        => !closing && !operationBusy && !dragging && !navigating && !modalDialogOpen &&
           scriptSeek is null;

    private void QueueNaturalCompletion()
    {
        if (!CanContinuePlayback() || completionQueued || player.NaturalCompletion is null) return;
        completionQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            completionQueued = false;
            if (!CanContinuePlayback() || player.NaturalCompletion is not object token) return;
            await RunActionAsync(async () =>
            {
                if (!player.ConsumeNaturalCompletion(token) || loadedTrack is null) return;
                int index = NextPlaybackIndex(appSettings.PlaybackMode, Playlist.Tracks.IndexOf(loadedTrack), Playlist.Tracks.Count);
                if (index < 0) return;
                AudioTrack next = Playlist.Tracks[index];
                if (ReferenceEquals(next, loadedTrack))
                {
                    await player.PlayAsync();
                }
                else
                {
                    await LoadTrackAsync(next);
                    if (!closing) TrackList.ScrollIntoView(next);
                }
            });
        }));
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (operationBusy || closing || modalDialogOpen) return;

        if (!settingsFileReady)
        {
            ToastMessages.Show("配置文件不可用，请修复文件或目录权限后重新启动程序。", ToastLevel.Error);
            return;
        }
        var dialog = new SettingsWindow(appSettings) { Owner = this };
        bool? confirmed;
        modalDialogOpen = true;
        try
        {
            await RestoreCanceledScriptSeekAsync();
            if (closing) return;
            confirmed = dialog.ShowDialog();
        }
        finally { modalDialogOpen = false; QueueNaturalCompletion(); }
        if (confirmed != true) return;

        try { settingsStore.Save(dialog.Settings); }
        catch (Exception ex) when (IsSettingsFileError(ex))
        {
            ToastMessages.Show($"配置保存失败，参数未更改：{ex.Message}", ToastLevel.Error);
            return;
        }
        bool detectionChanged = appSettings.FrameMilliseconds != dialog.Settings.FrameMilliseconds
            || appSettings.MinimumSilenceMilliseconds != dialog.Settings.MinimumSilenceMilliseconds
            || appSettings.SilenceThresholdDb != dialog.Settings.SilenceThresholdDb;
        appSettings = dialog.Settings;
        ScriptHost.SetAppearance(appSettings.ScriptAppearance);
        UpdatePlaybackModeButton();
        if (detectionChanged && loadedTrack is not null)
        {
            if (Silence.IsTrimming) Silence.CancelAnalysis(); // Defer the new settings' analysis until trimming ends.
            else StartAnalysis(loadedTrack);
        }
        RefreshSelectedCache();
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsPlaybackShortcut(e.Key, Keyboard.Modifiers)) return;
        e.Handled = true;
        if (e.IsRepeat) return;
        await HandleShortcutAsync(e.Key, Keyboard.Modifiers);
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (IsPlaybackShortcut(e.Key, Keyboard.Modifiers)) e.Handled = true;
    }

    internal static bool IsPlaybackShortcut(Key key, ModifierKeys modifiers)
        => (modifiers == ModifierKeys.None && key == Key.Space)
            || ((modifiers == ModifierKeys.None || modifiers == ModifierKeys.Control)
                && (key == Key.Left || key == Key.Right));

    internal Task HandleShortcutAsync(Key key, ModifierKeys modifiers = ModifierKeys.None)
    {
        if (modalDialogOpen || !IsPlaybackShortcut(key, modifiers)) return Task.CompletedTask;
        if (key == Key.Space) return ToggleFromUserAsync();
        bool timed = modifiers == ModifierKeys.Control;
        if (closing || dragging || !player.IsLoaded || (operationBusy && !navigating)
            || (!timed && (Silence.IsAnalyzing || !Silence.AnalysisReady))) return Task.CompletedTask;
        if (timed && (key == Key.Left ? appSettings.BackwardStepSeconds : appSettings.ForwardStepSeconds) == 0)
            return Task.CompletedTask;
        navigationKeys.Enqueue((key, timed));
        if (!navigating)
        {
            navigating = true;
            navigationTask = DrainNavigationAsync();
        }
        return navigationTask;
    }

    private async Task DrainNavigationAsync()
    {
        try
        {
            while (navigationKeys.Count > 0 && !closing)
            {
                var (key, timed) = navigationKeys.Dequeue();
                await RunActionAsync(async () =>
                {
                    TimeSpan current = player.GetCurrentTime();
                    TimeSpan? target = timed
                        ? current + TimeSpan.FromSeconds(key == Key.Left ? -appSettings.BackwardStepSeconds : appSettings.ForwardStepSeconds)
                        : key == Key.Left
                        ? Silence.FindPrevious(current, appSettings.NavigationToleranceMilliseconds)
                        : Silence.FindNext(current, appSettings.NavigationToleranceMilliseconds);
                    // An actual user seek supersedes any pending natural continuation.
                    if (target.HasValue) await player.SeekAsync(target.Value);
                }, ReaderAction.PreservePlayback);
            }
        }
        finally { navigationKeys.Clear(); navigating = false; QueueNaturalCompletion(); }
    }

    private async Task RunActionAsync(Func<Task> action, ReaderAction readerAction = ReaderAction.DiscardPreview)
    {
        if (operationBusy || closing) return;
        operationBusy = true;
        var preview = TakeScriptSeek();
        bool resume = preview?.Resume == true;
        bool committed = false;
        UpdateControls();

        async Task ExecuteAsync()
        {
            try
            {
                // During a preview the audio is already paused. A toggle from
                // the prior playing state must therefore leave it paused.
                if (!(readerAction == ReaderAction.TogglePlayback && resume))
                    await action();
                if (resume && (readerAction is ReaderAction.PreservePlayback or ReaderAction.CommitPreview) &&
                    !closing && player.LastError is null && player.IsLoaded &&
                    player.IsPaused && player.GetCurrentTime() < player.GetDuration())
                    await player.PlayAsync();
                committed = readerAction == ReaderAction.CommitPreview && player.LastError is null;
            }
            catch (Exception ex)
            {
                if (!closing) ToastMessages.Show(ex.Message, ToastLevel.Error);
            }
        }

        Task work = ExecuteAsync();
        activeOperation = work;
        try { await work; }
        finally
        {
            activeOperation = null;
            operationBusy = false;
            if (!closing)
            {
                // Every completed host command publishes actual playback state,
                // invalidating old reader requests even when position is unchanged.
                SynchronizeScript(reconcile: true, committedTarget: committed ? preview?.Target : null);
                UpdateControls();
                RefreshProgress();
                QueueNaturalCompletion();
            }
        }
    }

    private void Player_StateChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Player_StateChanged(sender, e)); return; }
        if (closing) return;
        if (player.LastError is null) displayedPlaybackError = null;
        else if (!ReferenceEquals(player.LastError, displayedPlaybackError))
        {
            displayedPlaybackError = player.LastError;
            TakeScriptSeek();
            if (!operationBusy) SynchronizeScript(reconcile: true);
            ToastMessages.Show("播放设备异常：" + player.LastError.Message, ToastLevel.Error);
        }
        UpdateControls();
        RefreshProgress();
    }

    private void UpdateControls()
    {
        bool available = !operationBusy && !closing;
        OpenButton.IsEnabled = available;
        ScriptButton.IsEnabled = available;
        SettingsButton.IsEnabled = available;
        PlaybackModeButton.IsEnabled = available && settingsFileReady;
        RefreshButton.IsEnabled = available && !Silence.IsTrimming && Playlist.RootFolder is not null;
        TrimButton.IsEnabled = !closing && !operationBusy && !Silence.IsAnalyzing && !Silence.IsTrimming && Silence.CanTrimSelected;
        TrackList.IsEnabled = available;
        PlayButton.IsEnabled = available && (player.IsLoaded || Playlist.Tracks.Count > 0);
        StopButton.IsEnabled = available && player.IsLoaded;
        ProgressSlider.IsEnabled = available && player.IsLoaded;
        bool playing = player.IsPlaying || scriptSeek?.Resume == true;
        PlayIconImage.Source = (ImageSource)FindResource(playing ? "PauseIcon" : "PlayIcon");
        PlayButton.ToolTip = playing ? "暂停" : "播放";
        AutomationProperties.SetName(PlayButton, playing ? "暂停" : "播放");
        if (!ReferenceEquals(indicatedTrack, loadedTrack))
        {
            if (indicatedTrack is not null)
            {
                indicatedTrack.IsPlaying = false;
                indicatedTrack.IsPaused = false;
            }
            indicatedTrack = loadedTrack;
        }
        if (indicatedTrack is not null)
        {
            indicatedTrack.IsPlaying = playing;
            indicatedTrack.IsPaused = !playing && player.IsPaused;
        }
    }

    private void RefreshProgress()
    {
        if (closing || scriptSeek is not null) return;
        bool scriptVisible = ScriptHost.IsVisible && WindowState != WindowState.Minimized;
        var interval = TimeSpan.FromMilliseconds(scriptVisible && player.IsPlaying ? ReaderPlaybackUpdateMs : NormalProgressUpdateMs);
        if (timer.Interval != interval) timer.Interval = interval;
        if (!player.IsLoaded || loadedTrack is null) ScriptHost.SetAudio(null);
        // The reader receives no intermediate engine states during an async
        // command or main-slider gesture. Reconcile once the command finishes.
        if (operationBusy || dragging) return;
        if (scriptVisible) SynchronizeScript();
        double duration = player.GetDuration().TotalSeconds;
        ProgressSlider.Maximum = Math.Max(duration, 0.001);
        ProgressSlider.Value = Math.Min(player.GetCurrentTime().TotalSeconds, duration);
        UpdateTimeText();
    }

    private void SynchronizeScript(bool reconcile = false, double? committedTarget = null)
    {
        if (!ScriptHost.HasWindow) return;
        ScriptHost.SetAudio(player.IsLoaded ? loadedTrack?.FilePath : null);
        double position = player.GetCurrentTime().TotalSeconds;
        bool reset = !player.IsLoaded || (!player.IsPlaying && !player.IsPaused &&
            (position <= 0 || position >= player.GetDuration().TotalSeconds));
        ScriptHost.UpdatePlayback(position, player.IsPlaying, reset, reconcile, committedTarget);
    }

    private void PlaylistBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        PlaylistBorder.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 8, 8);
    }

    private void UpdateTimeText() => TimeText.Text = AudioTrack.FormatTime(TimeSpan.FromSeconds(ProgressSlider.Value)) + " / " + AudioTrack.FormatTime(player.GetDuration());

    private void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsInitialized) UpdateTimeText();
    }

    private void ProgressSlider_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (operationBusy || closing || !player.IsLoaded) return;
        // Keep the pending temporary pause until the slider commits its seek.
        scriptSeekTimer.Stop();
        dragging = true;
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source is not null && !ReferenceEquals(source, ProgressSlider))
        {
            if (source is Thumb) return; // Thumb manages its own drag capture.
            source = source is Visual ? VisualTreeHelper.GetParent(source) : null;
        }
        ProgressSlider.CaptureMouse();
    }

    private async void ProgressSlider_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) await FinishDragAsync();
    }
    private async void ProgressSlider_LostMouseCapture(object sender, MouseEventArgs e) => await FinishDragAsync();

    private async Task FinishDragAsync()
    {
        if (!dragging) return;
        TimeSpan target = TimeSpan.FromSeconds(ProgressSlider.Value);
        dragging = false;
        if (ReferenceEquals(Mouse.Captured, ProgressSlider)) ProgressSlider.ReleaseMouseCapture();
        await RunActionAsync(() => player.SeekAsync(target), ReaderAction.PreservePlayback);
    }

    private void ScriptButton_Click(object sender, RoutedEventArgs e)
    {
        if (closing || modalDialogOpen || operationBusy || WindowState == WindowState.Minimized) return;
        if (ScriptHost.Toggle(appSettings.ScriptAppearance))
        {
            SynchronizeScript(reconcile: true);
            RefreshProgress();
        }
    }

    private void UpdateScriptButton()
    {
        string label = ScriptHost.IsVisible ? "隐藏原文" : "显示原文";
        ScriptButton.ToolTip = label;
        AutomationProperties.SetName(ScriptButton, label);
    }

    internal void PreviewScriptSeek(double seconds)
    {
        // Busy commands and the main slider already own navigation. Their
        // completion will reconcile the reader; do not send competing replies.
        if (closing || operationBusy || dragging || navigating) return;
        if (modalDialogOpen || !player.IsLoaded || player.LastError is not null ||
            player.NaturalCompletion is not null || !ScriptHost.IsVisible)
        {
            _ = RestoreCanceledScriptSeekAsync();
            return;
        }
        BeginScriptSeek(seconds);
    }

    internal void BeginScriptSeek(double seconds)
    {
        if (closing || operationBusy || !double.IsFinite(seconds) || seconds < 0 ||
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
                TakeScriptSeek();
                SynchronizeScript(reconcile: true);
                UpdateControls();
                RefreshProgress();
                ToastMessages.Show(ex.Message, ToastLevel.Error);
                return;
            }
        }
        // A device error or natural completion may have canceled the preview
        // during Pause; never recreate it from the old local value.
        if (!ReferenceEquals(scriptSeek, preview)) return;
        if (player.LastError is not null || player.NaturalCompletion is not null)
        {
            TakeScriptSeek();
            SynchronizeScript(reconcile: true);
            UpdateControls();
            RefreshProgress();
            QueueNaturalCompletion();
            return;
        }
        ProgressSlider.Value = target;
        scriptSeekTimer.Stop();
        scriptSeekTimer.Start();
    }

    internal async Task CommitScriptSeekAsync()
    {
        scriptSeekTimer.Stop();
        if (scriptSeek is not { } preview) return;
        if (closing || operationBusy || modalDialogOpen || dragging || !player.IsLoaded || player.LastError is not null)
        {
            await RestoreCanceledScriptSeekAsync();
            return;
        }
        await RunActionAsync(() => player.SeekAsync(TimeSpan.FromSeconds(preview.Target)), ReaderAction.CommitPreview);
    }

    private ScriptSeek? TakeScriptSeek()
    {
        scriptSeekTimer.Stop();
        var preview = scriptSeek;
        scriptSeek = null;
        return preview;
    }

    internal async Task RestoreCanceledScriptSeekAsync()
    {
        if (closing || operationBusy || dragging) return;
        var preview = TakeScriptSeek();
        if (preview?.Resume == true && player.LastError is null && player.IsPaused)
            await RunActionAsync(player.PlayAsync);
        else
        {
            SynchronizeScript(reconcile: true);
            UpdateControls();
            RefreshProgress();
            QueueNaturalCompletion();
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        TakeScriptSeek();
        ScriptHost.Dispose();
        Playlist.CancelPending();
        Silence.CancelPending();
        timer.Stop();
        ToastMessages.Clear();
        UpdateControls();
        try
        {
            if (activeOperation is not null) await activeOperation;
            await navigationTask;
            await Silence.WaitForPendingAsync();
            await player.CloseAsync();
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
        finally
        {
            player.StateChanged -= Player_StateChanged;
            player.NaturalPlaybackEnded -= Player_NaturalPlaybackEnded;
            player.Dispose();
            Playlist.Dispose();
            allowClose = true;
            Close();
        }
    }
}
