using System.Collections.ObjectModel;
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

namespace AudioPausePlayer;

public partial class MainWindow : Window
{
    private readonly AudioPlayer player = new();
    private readonly ObservableCollection<AudioTrack> tracks = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private SilenceSettings silenceSettings = new();
    private readonly SettingsStore settingsStore;
    private bool settingsFileReady;
    private List<SilenceRange> ranges = new();
    private AudioTrack? loadedTrack;
    private bool operationBusy;
    private bool analyzing;
    private bool trimming;
    private bool selectedCanTrim;
    private int analysisGeneration;
    private int selectionGeneration;
    private CancellationTokenSource? selectionCancellation;
    private Task? selectionTask;
    private readonly List<Task> runningSelectionChecks = new();
    private readonly List<Task> runningAnalyses = new();
    private ToastHost.ToastMessage? analysisToast;
    private ToastHost.ToastMessage? trimToast;
    private string? currentFolder;
    private CancellationTokenSource? trimCancellation;
    private Task? trimTask;
    private bool analysisReady;
    private bool dragging;
    private bool closing;
    private bool allowClose;
    private Task? activeOperation;
    private Task? analysisTask;
    private CancellationTokenSource? analysisCancellation;
    private readonly Queue<Key> navigationKeys = new();
    private bool navigating;
    private Task navigationTask = Task.CompletedTask;
    private Exception? displayedPlaybackError;

    public MainWindow() : this(new SettingsStore())
    {
    }

    internal MainWindow(SettingsStore settingsStore)
    {
        this.settingsStore = settingsStore;
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
        TrackList.ItemsSource = tracks;
        player.StateChanged += Player_StateChanged;
        timer.Tick += (_, _) => RefreshProgress();
        timer.Start();
        try
        {
            silenceSettings = settingsStore.LoadOrCreate();
            settingsFileReady = true;
        }
        catch (Exception ex) when (IsSettingsFileError(ex))
        {
            ToastMessages.Show($"配置读取或初始化失败，暂用默认参数：{ex.Message}", ToastLevel.Error);
        }
    }

    private static bool IsSettingsFileError(Exception exception)
        => exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException;

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (operationBusy || closing) return;
        var dialog = new OpenFolderDialog { Title = "选择教材音频文件夹" };
        if (dialog.ShowDialog(this) == true) await RunActionAsync(() => LoadFolderAsync(dialog.FolderName));
    }

    internal async Task LoadFolderAsync(string path)
    {
        // Keep the current folder and player if scanning the replacement folder fails.
        var files = await Task.Run(() => AudioLibrary.LoadFolder(path));
        if (closing) return;
        CancelAnalysis();
        CancelSelectionCheck();
        await player.CloseAsync();
        currentFolder = Path.GetFullPath(path);
        loadedTrack = null;
        tracks.Clear();
        foreach (var file in files) tracks.Add(file);
        int errors = files.Count(file => file.LoadError is not null);
        if (errors > 0) ToastMessages.Show($"{errors} 个文件的标签读取失败，悬停对应行可查看原因。", ToastLevel.Warning);
        else if (files.Count == 0) ToastMessages.Show("此文件夹中没有 WAV、MP3 或 M4A 音频文件。", ToastLevel.Warning);
        RefreshProgress();
        UpdateControls();
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
        CancelAnalysis();
        // OpenAsync releases the previous reader even if the replacement cannot open.
        loadedTrack = null;
        await player.OpenAsync(track.FilePath);
        loadedTrack = track;
        if (closing) return;
        await player.PlayAsync();
        if (!trimming) StartAnalysis(track);
    }

    private static SilenceSettings SnapshotSettings(SilenceSettings source) => new()
    {
        FrameMilliseconds = source.FrameMilliseconds,
        MinimumSilenceMilliseconds = source.MinimumSilenceMilliseconds,
        SilenceThresholdDb = source.SilenceThresholdDb,
        NavigationToleranceMilliseconds = source.NavigationToleranceMilliseconds,
        AllowedSilenceSeconds = source.AllowedSilenceSeconds
    };

    private void CancelAnalysis()
    {
        analysisGeneration++;
        analysisCancellation?.Cancel();
        if (analysisToast is not null) ToastMessages.CancelTask(analysisToast);
        analysisToast = null;
        analyzing = false;
        analysisReady = false;
        ranges.Clear();
    }

    private void StartAnalysis(AudioTrack track)
    {
        CancelAnalysis();
        var cancellation = new CancellationTokenSource();
        analysisCancellation = cancellation;
        int generation = analysisGeneration;
        analyzing = true;
        UpdateControls();
        Task work = AnalyzeAsync(track, SnapshotSettings(silenceSettings), generation, cancellation);
        analysisTask = work;
        runningAnalyses.RemoveAll(t => t.IsCompleted);
        runningAnalyses.Add(work);
    }

    private bool IsCurrentAnalysis(AudioTrack track, int generation, CancellationTokenSource cancellation)
        => !closing && !cancellation.IsCancellationRequested && generation == analysisGeneration
           && ReferenceEquals(track, loadedTrack);

    private async Task AnalyzeAsync(AudioTrack track, SilenceSettings settings, int generation,
        CancellationTokenSource cancellation)
    {
        ToastHost.ToastMessage? toast = null;
        try
        {
            // A hit must not generate an imaginary progress notification or run the PCM detector.
            SilenceAnalysis? cached = await Task.Run(
                () => SilenceResultStore.TryLoadVerified(track.FilePath, settings, cancellation.Token),
                cancellation.Token);
            if (!IsCurrentAnalysis(track, generation, cancellation)) return;
            SilenceAnalysis result;
            if (cached is not null)
            {
                result = cached;
            }
            else
            {
                toast = ToastMessages.ShowTask(
                    $"正在检测 {Path.GetFileName(track.FilePath)} 的停顿，请等待检测完成。");
                analysisToast = toast;
                result = await Task.Run(
                    () => SilenceResultStore.LoadOrDetect(track.FilePath, settings, cancellation.Token),
                    cancellation.Token);
            }
            if (!IsCurrentAnalysis(track, generation, cancellation)) return;
            ranges = result.Ranges.ToList();
            analysisReady = true;
            if (toast is not null)
            {
                if (!result.Saved)
                    ToastMessages.CompleteTask(toast, "检测完成，但无法保存 silence.json；本次仍可导航，裁剪不可用。", ToastLevel.Warning);
                else
                {
                    var cuts = SilenceTrimmer.PlanCuts(result.Ranges, result.Fingerprint.SampleRate,
                        settings.AllowedSilenceSeconds);
                    double removed = cuts.Sum(c => c.CutEnd - c.CutStart)
                        / (double)result.Fingerprint.SampleRate;
                    ToastMessages.CompleteTask(toast,
                        $"检测到 {ranges.Count} 段停顿，可压缩 {cuts.Count} 段，预计减少 {removed:F1} 秒。",
                        ToastLevel.Info);
                }
                if (ReferenceEquals(analysisToast, toast)) analysisToast = null;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsCurrentAnalysis(track, generation, cancellation))
            {
                if (toast is not null)
                    ToastMessages.CompleteTask(toast, "停顿检测失败，普通播放仍可用：" + ex.Message, ToastLevel.Error);
                else ToastMessages.Show("停顿检测失败，普通播放仍可用：" + ex.Message, ToastLevel.Error);
                if (ReferenceEquals(analysisToast, toast)) analysisToast = null;
            }
        }
        finally
        {
            if (IsCurrentAnalysis(track, generation, cancellation))
            {
                analyzing = false;
                UpdateControls();
                RefreshSelectedCache();
            }
            if (ReferenceEquals(analysisCancellation, cancellation)) analysisCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelSelectionCheck()
    {
        selectionGeneration++;
        selectionCancellation?.Cancel();
        selectedCanTrim = false;
    }

    private void TrackList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => RefreshSelectedCache();

    private void RefreshSelectedCache()
    {
        CancelSelectionCheck();
        if (closing) return;
        UpdateControls();
        if (TrackList.SelectedItem is not AudioTrack track) return;
        var cancellation = new CancellationTokenSource();
        selectionCancellation = cancellation;
        int generation = selectionGeneration;
        Task work = CheckSelectedCacheAsync(track, SnapshotSettings(silenceSettings), generation, cancellation);
        selectionTask = work;
        runningSelectionChecks.RemoveAll(task => task.IsCompleted);
        runningSelectionChecks.Add(work);
    }

    private async Task CheckSelectedCacheAsync(AudioTrack track, SilenceSettings settings, int generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            bool available = await Task.Run(() =>
            {
                SilenceAnalysis? cached =
                    SilenceResultStore.TryLoadVerified(track.FilePath, settings, cancellation.Token);
                return cached is not null && SilenceTrimmer.PlanCuts(
                    cached.Ranges, cached.Fingerprint.SampleRate, settings.AllowedSilenceSeconds).Count > 0;
            }, cancellation.Token);
            if (!closing && !cancellation.IsCancellationRequested && generation == selectionGeneration
                && ReferenceEquals(track, TrackList.SelectedItem))
            {
                selectedCanTrim = available;
                UpdateControls();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            // Unreadable/mismatched caches cannot enable trimming. Playback can still proceed.
        }
        finally
        {
            if (ReferenceEquals(selectionCancellation, cancellation)) selectionCancellation = null;
            cancellation.Dispose();
        }
    }

    private async void TrimButton_Click(object sender, RoutedEventArgs e)
    {
        if (trimming || analyzing || closing || !selectedCanTrim) return;
        if (TrackList.SelectedItem is not AudioTrack track) return;

        SilenceSettings settings = SnapshotSettings(silenceSettings);
        string source = track.FilePath;
        trimming = true;
        trimCancellation = new CancellationTokenSource();
        UpdateControls();
        trimToast = ToastMessages.ShowTask(
            $"正在压缩 {Path.GetFileName(source)} 的长静音，请等待裁剪完成。");
        Task<SilenceTrimResult> work = SilenceTrimmer.TrimAsync(source, settings, trimCancellation.Token);
        trimTask = work;
        try
        {
            SilenceTrimResult result = await work;
            if (closing) return;
            if (currentFolder is not null &&
                string.Equals(currentFolder, Path.GetDirectoryName(result.OutputPath), StringComparison.OrdinalIgnoreCase))
            {
                var added = AudioLibrary.ReadTrack(result.OutputPath);
                int index = 0;
                while (index < tracks.Count &&
                    AudioLibrary.CompareNames(Path.GetFileName(tracks[index].FilePath),
                        Path.GetFileName(result.OutputPath)) < 0)
                    index++;
                tracks.Insert(index, added);
            }
            if (!result.OutputSilenceSaved)
                ToastMessages.CompleteTask(trimToast!, "音频已保存，但停顿信息更新失败：" + result.OutputPath,
                    ToastLevel.Warning, TimeSpan.FromSeconds(15));
            else
                ToastMessages.CompleteTask(trimToast!,
                    $"裁剪完成：{result.OutputPath}。压缩 {result.TrimmedRangeCount} 段，减少 " +
                    $"{result.RemovedDuration.TotalSeconds:F3} 秒；原时长 " +
                    $"{AudioTrack.FormatTime(result.OriginalDuration)}，新时长 {AudioTrack.FormatTime(result.OutputDuration)}。",
                    ToastLevel.Info, TimeSpan.FromSeconds(15));
        }
        catch (FfmpegUnavailableException ex)
        {
            if (!closing && trimToast is not null)
                ToastMessages.CompleteTask(trimToast, ex.Message, ToastLevel.Error, TimeSpan.FromSeconds(15));
        }
        catch (OperationCanceledException)
        {
            if (!closing && trimToast is not null)
                ToastMessages.CompleteTask(trimToast, "长静音裁剪已取消。", ToastLevel.Info);
        }
        catch (Exception ex)
        {
            if (!closing && trimToast is not null)
                ToastMessages.CompleteTask(trimToast, "裁剪失败：" + ex.Message, ToastLevel.Error);
        }
        finally
        {
            trimToast = null;
            trimTask = null;
            trimCancellation?.Dispose();
            trimCancellation = null;
            trimming = false;
            if (!closing)
            {
                if (loadedTrack is not null && !analysisReady && !analyzing)
                    StartAnalysis(loadedTrack);
                RefreshSelectedCache();
                UpdateControls();
            }
        }
    }

    private async Task TogglePlaybackAsync()
    {
        if (player.IsLoaded)
        {
            if (player.IsPlaying) player.Pause();
            else await player.PlayAsync();
        }
        else
        {
            var track = TrackList.SelectedItem as AudioTrack ?? tracks.FirstOrDefault();
            if (track is not null) await LoadTrackAsync(track);
        }
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e) => await RunActionAsync(TogglePlaybackAsync);
    private async void StopButton_Click(object sender, RoutedEventArgs e) => await RunActionAsync(player.StopAsync);

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (operationBusy || closing) return;

        if (!settingsFileReady)
        {
            ToastMessages.Show("配置文件不可用，请修复文件或目录权限后重新启动程序。", ToastLevel.Error);
            return;
        }
        var dialog = new SilenceSettingsWindow(silenceSettings) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        try { settingsStore.Save(dialog.Settings); }
        catch (Exception ex) when (IsSettingsFileError(ex))
        {
            ToastMessages.Show($"配置保存失败，参数未更改：{ex.Message}", ToastLevel.Error);
            return;
        }
        bool detectionChanged = silenceSettings.FrameMilliseconds != dialog.Settings.FrameMilliseconds
            || silenceSettings.MinimumSilenceMilliseconds != dialog.Settings.MinimumSilenceMilliseconds
            || silenceSettings.SilenceThresholdDb != dialog.Settings.SilenceThresholdDb;
        silenceSettings = dialog.Settings;
        if (detectionChanged && loadedTrack is not null)
        {
            if (trimming) CancelAnalysis(); // Defer the new settings' analysis until trimming ends.
            else StartAnalysis(loadedTrack);
        }
        RefreshSelectedCache();
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None || !IsPlaybackKey(e.Key)) return;
        e.Handled = true;
        if (e.IsRepeat) return;
        await HandleShortcutAsync(e.Key);
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None && IsPlaybackKey(e.Key)) e.Handled = true;
    }

    private static bool IsPlaybackKey(Key key) => key == Key.Space || key == Key.Left || key == Key.Right;

    internal Task HandleShortcutAsync(Key key)
    {
        if (key == Key.Space) return RunActionAsync(TogglePlaybackAsync);
        if (key != Key.Left && key != Key.Right) return Task.CompletedTask;
        if (closing || dragging || analyzing || !analysisReady || (operationBusy && !navigating)) return Task.CompletedTask;
        navigationKeys.Enqueue(key);
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
                Key key = navigationKeys.Dequeue();
                await RunActionAsync(async () =>
                {
                    TimeSpan current = player.GetCurrentTime();
                    TimeSpan? target = key == Key.Left
                        ? SilenceDetector.FindPrevious(ranges, current, silenceSettings)
                        : SilenceDetector.FindNext(ranges, current, silenceSettings);
                    if (target.HasValue) await player.SeekAsync(target.Value);
                });
            }
        }
        finally { navigationKeys.Clear(); navigating = false; }
    }

    private async Task RunActionAsync(Func<Task> action)
    {
        if (operationBusy || closing) return;
        operationBusy = true;
        UpdateControls();
        Task work = RunCoreAsync(action);
        activeOperation = work;
        try { await work; }
        finally
        {
            activeOperation = null;
            operationBusy = false;
            if (!closing) { UpdateControls(); RefreshProgress(); }
        }
    }

    private async Task RunCoreAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            if (!closing)
            {
                ToastMessages.Show(ex.Message, ToastLevel.Error);
            }
        }
    }

    private void Player_StateChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Player_StateChanged(sender, e)); return; }
        if (closing) return;
        UpdateControls();
        RefreshProgress();
        if (player.LastError is null) displayedPlaybackError = null;
        else if (!ReferenceEquals(player.LastError, displayedPlaybackError))
        {
            displayedPlaybackError = player.LastError;
            ToastMessages.Show("播放设备异常：" + player.LastError.Message, ToastLevel.Error);
        }
    }

    private void UpdateControls()
    {
        bool available = !operationBusy && !closing;
        OpenButton.IsEnabled = available;
        SettingsButton.IsEnabled = available;
        TrimButton.IsEnabled = !closing && !operationBusy && !analyzing && !trimming && selectedCanTrim;
        TrackList.IsEnabled = available;
        PlayButton.IsEnabled = available && (player.IsLoaded || tracks.Count > 0);
        StopButton.IsEnabled = available && player.IsLoaded;
        ProgressSlider.IsEnabled = available && player.IsLoaded;
        bool playing = player.IsPlaying;
        PlayIconImage.Source = (ImageSource)FindResource(playing ? "PauseIcon" : "PlayIcon");
        PlayButton.ToolTip = playing ? "暂停" : "播放";
        AutomationProperties.SetName(PlayButton, playing ? "暂停" : "播放");
        foreach (var track in tracks)
        {
            bool loaded = ReferenceEquals(track, loadedTrack);
            track.IsPlaying = loaded && player.IsPlaying;
            track.IsPaused = loaded && player.IsPaused;
        }
    }

    private void RefreshProgress()
    {
        if (dragging || closing) return;
        double duration = player.GetDuration().TotalSeconds;
        ProgressSlider.Maximum = Math.Max(duration, 0.001);
        ProgressSlider.Value = Math.Min(player.GetCurrentTime().TotalSeconds, duration);
        UpdateTimeText();
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
        await RunActionAsync(() => player.SeekAsync(target));
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        CancelAnalysis();
        CancelSelectionCheck();
        trimCancellation?.Cancel();
        timer.Stop();
        ToastMessages.Clear();
        UpdateControls();
        try
        {
            if (activeOperation is not null) await activeOperation;
            await navigationTask;
            await Task.WhenAll(runningAnalyses);
            await Task.WhenAll(runningSelectionChecks);
            if (trimTask is not null)
            {
                try { await trimTask; }
                catch (OperationCanceledException) { }
                catch (Exception) { /* TrimButton_Click handles task errors unless closing. */ }
            }
            await player.CloseAsync();
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
        finally
        {
            player.StateChanged -= Player_StateChanged;
            player.Dispose();
            allowClose = true;
            Close();
        }
    }
}
