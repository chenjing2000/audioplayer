using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

using AudioPlayer.Playback;
using AudioPlayer.Application;
using AudioPlayer.Library;
using AudioPlayer.Silence;
using AudioPlayer.Script;
using AudioPlayer.Settings;

namespace AudioPlayer.UI;

public partial class MainWindow : Window
{
    internal PlayerController Controller { get; }
    internal Playlist Playlist => Controller.Playlist;
    internal SilenceTasks Silence => Controller.Silence;
    private PlaybackSession Session => Controller.Session;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(NormalProgressUpdateMs) };
    private AudioTrack? indicatedTrack;
    private ToastHost.ToastMessage? analysisToast;
    private bool allowClose;
    internal ScriptWindowHost ScriptHost { get; }
    private const int ReaderPlaybackUpdateMs = 25;
    private const int NormalProgressUpdateMs = 100;

    public MainWindow() : this(new SettingsStore(),
        new PlaylistStore(PlaylistStore.GetFilePath(SettingsStore.GetExecutablePath())))
    {
    }

    internal MainWindow(SettingsStore settingsStore, PlaylistStore? playlistStore = null,
        Func<ScriptWindow>? scriptWindowFactory = null)
    {
        // Test settings paths keep their playlists isolated from the real executable.
        Controller = new PlayerController(Dispatcher, settingsStore, playlistStore ?? new PlaylistStore(
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
        Playlist.Error += message => { if (!Session.IsClosing) ToastMessages.Show(message, ToastLevel.Error); };
        Silence.StateChanged += UpdateControls;
        Silence.AnalysisCanceled += CancelAnalysisToast;
        Silence.AnalysisStarted += path => analysisToast = ToastMessages.ShowTask(
            $"正在检测 {Path.GetFileName(path)} 的停顿，请等待检测完成。");
        Silence.AnalysisCompleted += ReportAnalysis;
        Silence.AnalysisFailed += ReportAnalysisFailure;
        Controller.FolderLoaded += ReportFolderLoad;
        Controller.FolderStateChanged += RefreshUi;
        Controller.TrackAdvanced += track => TrackList.ScrollIntoView(track);
        Controller.Error += message => ToastMessages.Show(message, ToastLevel.Error);
        Session.Error += message => ToastMessages.Show(message, ToastLevel.Error);
        Session.StateChanged += RefreshUi;
        Session.Reconcile += target => SynchronizeScript(reconcile: true, committedTarget: target);
        Session.PreviewChanged += target => ProgressSlider.Value = target;
        timer.Tick += (_, _) => RefreshProgress();
        timer.Start();
        Controller.InitializeSettings();
        UpdatePlaybackModeButton();
        PlaybackModeButton.IsEnabled = Controller.SettingsFileReady;
        Loaded += RestorePlaylistOnLoad;
        ScriptHost.VisibilityChanged += UpdateScriptButton;
        ScriptHost.NavigationPreview += PreviewScriptSeek;
        ScriptHost.NavigationCanceled += async () => await Session.CancelPreviewAsync();
        ScriptHost.Shortcut += async (key, modifiers) => await HandleShortcutAsync(key, modifiers);
    }

    private async void RestorePlaylistOnLoad(object sender, RoutedEventArgs e)
    {
        Loaded -= RestorePlaylistOnLoad;
        await Controller.RestoreAsync();
    }

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (Session.IsBusy || Session.IsClosing || Session.ModalDialogOpen) return;
        var dialog = new OpenFolderDialog { Title = "选择教材音频文件夹" };
        Session.ModalDialogOpen = true;
        try
        {
            await Session.CancelPreviewAsync();
            if (Session.IsClosing) return;
            if (dialog.ShowDialog(this) == true)
                await Controller.OpenFolderAsync(dialog.FolderName);
        }
        finally { Session.ModalDialogOpen = false; Controller.QueueNaturalCompletion(); }
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
        if (folder is null || Session.IsBusy || Silence.IsTrimming || Session.IsClosing) return;
        await Controller.OpenFolderAsync(folder);
    }

    private async void TrackList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Session.IsBusy || Session.IsClosing || e.ChangedButton != MouseButton.Left) return;
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source is not null && source is not DataGridRow)
            source = VisualTreeHelper.GetParent(source);
        if (source is DataGridRow row && row.Item is AudioTrack track)
            await Controller.PlayAsync(track);
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
        if (Session.IsBusy || Session.IsClosing || row.Item is not AudioTrack track) return;
        await Controller.PlayAsync(track);
    }

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
        => Controller.SelectTrack(TrackList.SelectedItem as AudioTrack);

    private async void TrimButton_Click(object sender, RoutedEventArgs e)
    {
        if (Silence.IsTrimming || Silence.IsAnalyzing || Session.IsClosing || !Silence.CanTrimSelected) return;
        if (TrackList.SelectedItem is not AudioTrack track) return;

        var toast = ToastMessages.ShowTask(
            $"正在压缩 {Path.GetFileName(track.FilePath)} 的长静音，请等待裁剪完成。");
        try
        {
            await Controller.TrimSelectedAsync(result =>
            {
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
            if (!Session.IsClosing)
                ToastMessages.CompleteTask(toast, ex.Message, ToastLevel.Error, TimeSpan.FromSeconds(15));
        }
        catch (OperationCanceledException)
        {
            if (!Session.IsClosing)
                ToastMessages.CompleteTask(toast, "长静音裁剪已取消。", ToastLevel.Info);
        }
        catch (Exception ex)
        {
            if (!Session.IsClosing)
                ToastMessages.CompleteTask(toast, "裁剪失败：" + ex.Message, ToastLevel.Error);
        }
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e) => await Controller.ToggleAsync();
    private async void StopButton_Click(object sender, RoutedEventArgs e) => await Session.StopAsync();

    private void UpdatePlaybackModeButton()
    {
        PlaybackMode mode = Controller.Settings.PlaybackMode;
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
        if (Session.IsBusy || Session.IsClosing || Session.ModalDialogOpen || !Controller.SettingsFileReady) return;
        if (Controller.CyclePlaybackMode()) UpdatePlaybackModeButton();
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (Session.IsBusy || Session.IsClosing || Session.ModalDialogOpen) return;

        if (!Controller.SettingsFileReady)
        {
            ToastMessages.Show("配置文件不可用，请修复文件或目录权限后重新启动程序。", ToastLevel.Error);
            return;
        }
        var dialog = new SettingsWindow(Controller.Settings) { Owner = this };
        bool? confirmed;
        Session.ModalDialogOpen = true;
        try
        {
            await Session.CancelPreviewAsync();
            if (Session.IsClosing) return;
            confirmed = dialog.ShowDialog();
        }
        finally { Session.ModalDialogOpen = false; Controller.QueueNaturalCompletion(); }
        if (confirmed != true) return;

        if (Controller.ApplySettings(dialog.Settings))
        {
            ScriptHost.SetAppearance(Controller.Settings.ScriptAppearance);
            UpdatePlaybackModeButton();
        }
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
        if (Session.ModalDialogOpen || !IsPlaybackShortcut(key, modifiers)) return Task.CompletedTask;
        return key == Key.Space ? Controller.ToggleAsync()
            : Controller.NavigateAsync(key == Key.Left, modifiers == ModifierKeys.Control);
    }

    private void RefreshUi()
    {
        UpdateControls();
        RefreshProgress();
    }

    private void UpdateControls()
    {
        bool available = !Session.IsBusy && !Session.IsClosing;
        OpenButton.IsEnabled = available;
        ScriptButton.IsEnabled = available;
        SettingsButton.IsEnabled = available;
        PlaybackModeButton.IsEnabled = available && Controller.SettingsFileReady;
        RefreshButton.IsEnabled = available && !Silence.IsTrimming && Playlist.RootFolder is not null;
        TrimButton.IsEnabled = !Session.IsClosing && !Session.IsBusy && !Silence.IsAnalyzing && !Silence.IsTrimming && Silence.CanTrimSelected;
        TrackList.IsEnabled = available;
        PlayButton.IsEnabled = available && (Session.IsLoaded || Playlist.Tracks.Count > 0);
        StopButton.IsEnabled = available && Session.IsLoaded;
        ProgressSlider.IsEnabled = available && Session.IsLoaded;
        bool playing = Session.PlayRequested;
        PlayIconImage.Source = (ImageSource)FindResource(playing ? "PauseIcon" : "PlayIcon");
        PlayButton.ToolTip = playing ? "暂停" : "播放";
        AutomationProperties.SetName(PlayButton, playing ? "暂停" : "播放");
        if (!ReferenceEquals(indicatedTrack, Session.LoadedTrack))
        {
            if (indicatedTrack is not null)
            {
                indicatedTrack.IsPlaying = false;
                indicatedTrack.IsPaused = false;
            }
            indicatedTrack = Session.LoadedTrack;
        }
        if (indicatedTrack is not null)
        {
            indicatedTrack.IsPlaying = playing;
            indicatedTrack.IsPaused = !playing && Session.IsPaused;
        }
    }

    private void RefreshProgress()
    {
        if (Session.IsClosing || Session.PreviewPosition is not null) return;
        bool scriptVisible = ScriptHost.IsVisible && WindowState != WindowState.Minimized;
        var interval = TimeSpan.FromMilliseconds(scriptVisible && Session.IsPlaying ? ReaderPlaybackUpdateMs : NormalProgressUpdateMs);
        if (timer.Interval != interval) timer.Interval = interval;
        if (!Session.IsLoaded || Session.LoadedTrack is null) ScriptHost.SetAudio(null);
        // The reader receives no intermediate engine states during an async
        // command or main-slider gesture. Reconcile once the command finishes.
        if (Session.IsBusy || Session.IsDragging) return;
        if (scriptVisible) SynchronizeScript();
        double duration = Session.Duration.TotalSeconds;
        ProgressSlider.Maximum = Math.Max(duration, 0.001);
        ProgressSlider.Value = Math.Min(Session.Position.TotalSeconds, duration);
        UpdateTimeText();
    }

    private void SynchronizeScript(bool reconcile = false, double? committedTarget = null)
    {
        if (!ScriptHost.HasWindow) return;
        ScriptHost.SetAudio(Session.IsLoaded ? Session.LoadedTrack?.FilePath : null);
        ScriptHost.UpdatePlayback(Session.Position.TotalSeconds, Session.IsPlaying, Session.ResetReader, reconcile, committedTarget);
    }

    private void PlaylistBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        PlaylistBorder.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 8, 8);
    }

    private void UpdateTimeText() => TimeText.Text = AudioTrack.FormatTime(TimeSpan.FromSeconds(ProgressSlider.Value)) + " / " + AudioTrack.FormatTime(Session.Duration);

    private void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsInitialized) UpdateTimeText();
    }

    private void ProgressSlider_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (!Session.BeginDrag()) return;
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
        if (!Session.EndDrag()) return;
        TimeSpan target = TimeSpan.FromSeconds(ProgressSlider.Value);
        if (ReferenceEquals(Mouse.Captured, ProgressSlider)) ProgressSlider.ReleaseMouseCapture();
        await Session.SeekAsync(target);
    }

    private void ScriptButton_Click(object sender, RoutedEventArgs e)
    {
        if (Session.IsClosing || Session.ModalDialogOpen || Session.IsBusy || WindowState == WindowState.Minimized) return;
        if (ScriptHost.Toggle(Controller.Settings.ScriptAppearance))
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
        => Controller.PreviewScriptSeek(seconds, ScriptHost.IsVisible);

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (Session.IsClosing) return;
        Session.BeginClosing();
        ScriptHost.Dispose();
        timer.Stop();
        ToastMessages.Clear();
        UpdateControls();
        try { await Controller.ShutdownAsync(); }
        finally
        {
            allowClose = true;
            Close();
        }
    }
}
