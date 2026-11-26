using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioPlayer;
using AudioPlayer.Playback;
using AudioPlayer.Library;
using AudioPlayer.Silence;
using AudioPlayer.Script;
using AudioPlayer.Settings;
using AudioPlayer.UI;
using PlayerEngine = global::AudioPlayer.Playback.PlaybackEngine;
using NAudio.Wave;

internal static class UiChecks
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    public static Task<int> RunAsync(string audioFolder)
    {
        var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var appXml = System.Xml.Linq.XDocument.Load(Path.GetFullPath("AudioPlayer/App.xaml"));
            var dictionary = appXml.Root!.Elements().Single().Elements().Single();
            dictionary.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
            string resourceXml = dictionary.ToString();
            app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resourceXml);
            app.Startup += async (_, _) =>
            {
                MainWindow? window = null;
                int failed = 0, passed = 0;
                void Assert(bool value, string message)
                {
                    if (!value) throw new Exception(message);
                    passed++;
                    Console.WriteLine("PASS UI " + message);
                }
                try
                {
                    var settingsStore = new SettingsStore(Path.Combine(audioFolder, "UiPlayer-" + Guid.NewGuid().ToString("N") + ".settings.json"));
                    ScriptWindow? nextReader = null;
                    window = new MainWindow(settingsStore, scriptWindowFactory: () => nextReader ?? throw new InvalidOperationException("Missing test reader fixture"));
                    Assert(File.Exists(settingsStore.FilePath), "First launch initializes settings file");
                    Rect workArea = SystemParameters.WorkArea;
                    double expectedLeft = Math.Round(workArea.Left + (workArea.Width - window.Width) / 2);
                    double expectedTop = Math.Round(workArea.Top + (workArea.Height - window.Height) / 2);
                    Assert(Math.Abs(window.Left - expectedLeft) < 0.01 && Math.Abs(window.Top - expectedTop) < 0.01, "Window starts centered with rounded coordinates");
                    window.ShowInTaskbar = false; window.ShowActivated = false; window.Left = -20000; window.Top = -20000;
                    window.Show();
                    if (window.Controller.Session.ActiveOperation is Task startupAction) await startupAction;
                    // Exercise the same production gate independently of the window's
                    // asynchronous startup restoration. Verify the held command itself
                    // entered the gate, rather than observing an unrelated busy command.
                    using (var playbackSession = new PlaybackSession(window.Dispatcher))
                    {
                        var heldCommand = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        bool firstRan = false, overlappingRan = false;
                        Task firstCommand = playbackSession.RunActionAsync(() =>
                        {
                            firstRan = true;
                            return heldCommand.Task;
                        });
                        try
                        {
                            await playbackSession.RunActionAsync(() =>
                            {
                                overlappingRan = true;
                                return Task.CompletedTask;
                            });
                            Assert(firstRan && !firstCommand.IsCompleted && playbackSession.IsBusy &&
                                playbackSession.ActiveOperation is not null && !overlappingRan,
                                "Playback session rejects overlapping commands through the shared gate");
                        }
                        finally
                        {
                            heldCommand.TrySetResult();
                            await firstCommand;
                        }
                        bool nextRan = false;
                        await playbackSession.RunActionAsync(() =>
                        {
                            nextRan = true;
                            return Task.CompletedTask;
                        });
                        Assert(nextRan && !playbackSession.IsBusy && playbackSession.ActiveOperation is null,
                            "Playback session releases the shared gate after command completion");
                    }
                    // The native window rounds its dimensions to whole physical pixels.
                    double expectedWidth = Math.Clamp(Math.Round(SystemParameters.WorkArea.Width * 0.5), window.MinWidth, window.MaxWidth);
                    double expectedHeight = Math.Clamp(Math.Round(SystemParameters.WorkArea.Height * 0.6), window.MinHeight, window.MaxHeight);
                    Assert(Math.Abs(window.Width - expectedWidth) < 1 && Math.Abs(window.Height - expectedHeight) < 1,
                           "Default size uses rounded screen proportions within the window limits");
                    Assert(Math.Abs(window.MinWidth - 520) < 0.01 && Math.Abs(window.MinHeight - 420) < 0.01, "Minimum window size is 520x420");
                    Assert(double.IsPositiveInfinity(window.MaxWidth) && double.IsPositiveInfinity(window.MaxHeight), "Window has no application size ceiling");
                    int nativeStyle = GetWindowLong(new System.Windows.Interop.WindowInteropHelper(window).Handle, -16);
                    Assert((nativeStyle & 0x00010000) != 0, "Native maximize button is enabled");
                    Assert(window.ResizeMode == ResizeMode.CanResize, "Window remains resizable");
                    var toasts = (ToastHost)window.FindName("ToastMessages");
                    var toastStack = FindVisual<StackPanel>(toasts)!;
                    Assert(toastStack.Children.Count == 0, "Toast overlay starts empty");
                    // Give the queue-order test its own lifetime budget, independent of
                    // the separate expiration assertions and slow hosted rendering.
                    toasts.Show("测试信息", ToastLevel.Info, TimeSpan.FromSeconds(30));
                    Assert(toastStack.Children.Count == 1 &&
                        FindVisual<TextBlock>((Border)toastStack.Children[0])!.Text == "普通：测试信息",
                        "Normal toasts use the information prefix");
                    toasts.Show("测试警告", ToastLevel.Warning, TimeSpan.FromSeconds(30));
                    toasts.Show("测试错误", ToastLevel.Error);
                    Assert(toastStack.Children.Count == 3 &&
                        FindVisual<TextBlock>((Border)toastStack.Children[0])!.Text == "错误：测试错误",
                        "Newest visible toast is on top");
                    toasts.Show("排队消息", ToastLevel.Info, TimeSpan.FromSeconds(30));
                    Assert(toastStack.Children.Count == 3, "Only three toast slots are shown");
                    FindVisual<Button>((Border)toastStack.Children[2])!
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    bool QueueAdvanced() => toastStack.Children.Count == 3 &&
                        FindVisual<TextBlock>((Border)toastStack.Children[0])!.Text == "普通：排队消息";
                    var queueDeadline = System.Diagnostics.Stopwatch.StartNew();
                    while (!QueueAdvanced() && queueDeadline.Elapsed < TimeSpan.FromSeconds(10))
                        await Task.Delay(50);
                    Assert(QueueAdvanced(), "Closing the oldest toast dequeues the next message");

                    // Expiration is a separate behavior; use the real default lifetime.
                    toasts.Clear();
                    toasts.Show("测试错误", ToastLevel.Error);
                    toasts.Show("测试信息", ToastLevel.Info);
                    var expirationDeadline = System.Diagnostics.Stopwatch.StartNew();
                    while (toastStack.Children.Count > 1 && expirationDeadline.Elapsed < TimeSpan.FromSeconds(15))
                        await Task.Delay(50);
                    Assert(toastStack.Children.Count == 1 &&
                        FindVisual<TextBlock>((Border)toastStack.Children[0])!.Text == "错误：测试错误",
                        "Normal toast expires while error toast persists");
                    toasts.Clear();
                    var active = toasts.ShowTask("持续检测");
                    await Task.Delay(5300);
                    Assert(toastStack.Children.Count == 1, "Task toast remains visible");
                    toasts.CompleteTask(active, "检测完成", ToastLevel.Info, TimeSpan.FromMilliseconds(150));
                    var completionDeadline = System.Diagnostics.Stopwatch.StartNew();
                    while (toastStack.Children.Count != 0 && completionDeadline.Elapsed < TimeSpan.FromSeconds(10))
                        await Task.Delay(50);
                    Assert(toastStack.Children.Count == 0, "Completed task expires in the same slot");
                    var refreshButton = (Button)window.FindName("RefreshButton");
                    Assert(!refreshButton.IsEnabled, "Refresh is disabled until a folder is opened");
                    await window.Controller.LoadFolderAsync(audioFolder);
                    var list = (DataGrid)window.FindName("TrackList");
                    var slider = (Slider)window.FindName("ProgressSlider");
                    var timeText = (TextBlock)window.FindName("TimeText");
                    var openButton = (Button)window.FindName("OpenButton");
                    var playButton = (Button)window.FindName("PlayButton");
                    var stopButton = (Button)window.FindName("StopButton");
                    var settingsButton = (Button)window.FindName("SettingsButton");
                    var trimButton = (Button)window.FindName("TrimButton");
                    Assert(refreshButton.IsEnabled && refreshButton.ToolTip?.ToString() == "刷新文件列表",
                        "Refresh becomes available after opening a folder");

                    // Refresh must reuse the full folder-load path without preserving selection.
                    string refreshFolder = Path.Combine(audioFolder, "ui-refresh-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(refreshFolder);
                    try
                    {
                        string nested = Path.Combine(refreshFolder, "Book 2");
                        Directory.CreateDirectory(nested);
                        string original = Path.Combine(nested, "original.wav");
                        string renamed = Path.Combine(nested, "renamed.wav");
                        string added = Path.Combine(refreshFolder, "added.wav");
                        using (var writer = new WaveFileWriter(original, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
                            writer.WriteSamples(new float[44100], 0, 44100);
                        await window.Controller.LoadFolderAsync(refreshFolder);
                        Assert(list.Items.Count == 1 && refreshButton.IsEnabled, "Recursive folder load includes nested audio");
                        var playlistStore = new PlaylistStore(settingsStore.FilePath[..^".settings.json".Length] + ".playlist.json");
                        Assert(playlistStore.Load()!.Files.SequenceEqual(new[] { original }),
                            "Opening a directory persists the absolute nested path");
                        Assert(((AudioTrack)list.Items[0]).DisplayName == "original" &&
                            ((AudioTrack)list.Items[0]).RowToolTip == nested &&
                            ((DataGridTextColumn)list.Columns[1]).Binding is System.Windows.Data.Binding { Path.Path: "DisplayName" },
                            "Filename column omits extension while row tooltip shows parent directory");
                        list.SelectedIndex = 0;
                        File.Move(original, renamed);
                        using (var writer = new WaveFileWriter(added, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
                            writer.WriteSamples(new float[44100], 0, 44100);
                        refreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (window.Controller.Session.ActiveOperation is Task refreshAction) await refreshAction;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        Assert(list.Items.Count == 2 &&
                            list.Items.OfType<AudioTrack>().Select(track => Path.GetFileName(track.FilePath))
                                .SequenceEqual(new[] { "added.wav", "renamed.wav" }) &&
                            list.SelectedItem is null && !Field<PlayerEngine>(window.Controller.Session, "player")!.IsLoaded,
                            "Refresh reloads added and renamed files, clears selection and closes playback");
                        Assert(refreshButton.IsEnabled, "Refresh button re-enables after folder load");
                        File.Delete(added);
                        refreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (window.Controller.Session.ActiveOperation is Task removedAction) await removedAction;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        Assert(list.Items.Count == 1 &&
                            Path.GetFileName(((AudioTrack)list.Items[0]).FilePath) == "renamed.wav",
                            "Refresh drops deleted audio files");

                        string trimmed = Path.Combine(nested, "renamed.new.wav");
                        File.Copy(renamed, trimmed);
                        window.Controller.AddTrimmedOutput(trimmed);
                        window.Controller.AddTrimmedOutput(trimmed);
                        Assert(list.Items.Count == 2 && playlistStore.Load()!.Files.Contains(trimmed),
                            "Nested trim output inserts and persists exactly once");
                        string undiscovered = Path.Combine(nested, "new-file.wav");
                        File.Copy(renamed, undiscovered);
                        File.Delete(trimmed);
                        var reopened = new MainWindow(settingsStore, playlistStore)
                            { ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000 };
                        reopened.Show();
                        if (reopened.Controller.Session.ActiveOperation is Task restoreAction) await restoreAction;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        try
                        {
                            var restoredList = (DataGrid)reopened.FindName("TrackList");
                            Assert(restoredList.Items.Count == 1 &&
                                ((AudioTrack)restoredList.Items[0]).FilePath == renamed &&
                                reopened.Controller.Playlist.RootFolder == refreshFolder &&
                                !Field<PlayerEngine>(reopened.Controller.Session, "player")!.IsLoaded,
                                "Startup restores saved existing files without rescanning or playing");
                            Assert(playlistStore.Load()!.Files.SequenceEqual(new[] { renamed }),
                                "Startup prunes deleted saved paths");
                        }
                        finally { reopened.Close(); }
                        Directory.Delete(refreshFolder, true);
                        refreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (window.Controller.Session.ActiveOperation is Task failedAction) await failedAction;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        Assert(list.Items.Count == 0 && list.SelectedItem is null &&
                            window.Controller.Playlist.RootFolder is null &&
                            !Field<PlayerEngine>(window.Controller.Session, "player")!.IsLoaded &&
                            !refreshButton.IsEnabled && !playButton.IsEnabled && !stopButton.IsEnabled,
                            "Missing refresh folder clears playlist, folder path and playback controls");
                        Assert(playlistStore.Load() is { RootFolder: null, Files.Length: 0 },
                            "Failed scan persists the empty state");

                        // A failed scan from the Open Folder path clears the old state too.
                        await window.Controller.LoadFolderAsync(audioFolder);
                        Assert(refreshButton.IsEnabled &&
                            window.Controller.Playlist.RootFolder == Path.GetFullPath(audioFolder),
                            "Valid folder can reopen after failed refresh");
                        bool failedScan = false;
                        try { await window.Controller.LoadFolderAsync(refreshFolder); }
                        catch (DirectoryNotFoundException) { failedScan = true; }
                        Assert(failedScan && list.Items.Count == 0 &&
                            window.Controller.Playlist.RootFolder is null &&
                            !refreshButton.IsEnabled && !playButton.IsEnabled,
                            "Failed folder load clears previously loaded folder");
                        // A successfully scanned empty folder is not a scan failure.
                        string emptyFolder = Path.Combine(audioFolder, "ui-empty-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(emptyFolder);
                        try
                        {
                            await window.Controller.LoadFolderAsync(emptyFolder);
                            Assert(list.Items.Count == 0 &&
                                window.Controller.Playlist.RootFolder == Path.GetFullPath(emptyFolder) &&
                                refreshButton.IsEnabled,
                                "Empty valid folder retains folder path and Refresh");
                        }
                        finally { Directory.Delete(emptyFolder); }
                        toasts.Clear();
                        await window.Controller.LoadFolderAsync(audioFolder);
                    }
                    finally
                    {
                        if (Directory.Exists(refreshFolder)) Directory.Delete(refreshFolder, true);
                    }


                    // Exercise row realization and recycling without optional MP3 fixtures.
                    string scrollFolder = Path.Combine(audioFolder, "ui-scroll-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(scrollFolder);
                    try
                    {
                        string sample = Path.Combine(scrollFolder, "001.wav");
                        using (var writer = new WaveFileWriter(sample, WaveFormat.CreateIeeeFloatWaveFormat(8000, 1)))
                            writer.WriteSamples(new float[800], 0, 800);
                        for (int i = 2; i <= 120; i++)
                            File.Copy(sample, Path.Combine(scrollFolder, $"{i:D3}.wav"));
                        await window.Controller.LoadFolderAsync(scrollFolder);
                        Assert(list.Items.Count == 120 && list.EnableRowVirtualization && list.RowHeaderWidth == 0,
                            "Playlist keeps virtualization and eliminates row header width");
                        Assert(list.HeadersVisibility == DataGridHeadersVisibility.Column,
                            "Playlist displays column headers only");
                        foreach (int index in new[] { 0, 30, 75, 119, 40, 1, 0 })
                        {
                            list.ScrollIntoView(list.Items[index]);
                            list.UpdateLayout();
                            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                            list.UpdateLayout();
                            var row = list.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow;
                            Assert(row is not null, $"Scrolled row {index} is realized");
                            var header = FindVisual<System.Windows.Controls.Primitives.DataGridRowHeader>(row!);
                            Assert(row!.BorderThickness == new Thickness(0),
                                $"Scrolled row {index} has no row border");
                            Assert(header is null || !header.IsVisible || header.ActualWidth < 0.01,
                                $"Scrolled row {index} has no visible row header");
                            int alternation = ItemsControl.GetAlternationIndex(row);
                            Assert(alternation is 0 or 1
                                && ColorOf(row.Background) == (alternation == 0 ? "#FFFFFFFF" : "#FFF0F0F0"),
                                $"Scrolled row {index} follows its WPF alternation index {alternation}");
                            foreach (int neighbor in new[] { index - 1, index + 1 })
                            {
                                if (neighbor < 0 || neighbor >= list.Items.Count) continue;
                                if (list.ItemContainerGenerator.ContainerFromIndex(neighbor) is DataGridRow adjacent)
                                    Assert(ItemsControl.GetAlternationIndex(adjacent) != alternation,
                                        $"Realized adjacent rows {index} and {neighbor} alternate");
                            }
                        }
                        await window.Controller.LoadFolderAsync(audioFolder);
                    }
                    finally { Directory.Delete(scrollFolder, true); }

                    // Selection must inspect only existing results and never analyze an unopened audio file.
                    string resultFolder = Path.Combine(audioFolder, "ui-results-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(resultFolder);
                    try
                    {
                        string source = Path.Combine(resultFolder, "selected.wav");
                        using (var writer = new WaveFileWriter(source, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
                            writer.WriteSamples(new float[44100 * 4], 0, 44100 * 4);
                        await window.Controller.LoadFolderAsync(resultFolder);
                        list.SelectedIndex = 0;
                        if (window.Controller.Silence.SelectionTask is Task missingCheck) await missingCheck;
                        Assert(!trimButton.IsEnabled && !File.Exists(SilenceResultStore.GetPath(source)),
                            "Selecting uncached audio does not trigger detection or enable trimming");

                        Assert(SilenceResultStore.LoadOrDetect(source, new SilenceOptions()).Saved,
                            "Synthetic selection fixture can persist results");
                        // M4A decoders can report one PCM sample more than TotalTime at EOF.
                        string resultPath = SilenceResultStore.GetPath(source);
                        var resultNode = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(resultPath))!;
                        resultNode["ranges"]![0]!["endTicks"] = resultNode["durationTicks"]!.GetValue<long>() + 208;
                        File.WriteAllText(resultPath, resultNode.ToJsonString());
                        list.SelectedIndex = -1;
                        list.SelectedIndex = 0;
                        if (window.Controller.Silence.SelectionTask is Task validCheck) await validCheck;
                        Assert(trimButton.IsEnabled,
                            "Cached silence enables scissors even with a one-sample EOF rounding difference");

                        File.SetLastWriteTimeUtc(source, File.GetLastWriteTimeUtc(source).AddSeconds(5));
                        list.SelectedIndex = -1;
                        list.SelectedIndex = 0;
                        if (window.Controller.Silence.SelectionTask is Task outdatedCheck) await outdatedCheck;
                        Assert(!trimButton.IsEnabled, "Changed source invalidates selected trim availability");
                        for (int i = 0; i < 12; i++)
                        {
                            list.SelectedIndex = -1;
                            list.SelectedIndex = 0;
                        }
                        await window.Controller.Silence.WaitForPendingAsync();
                        Assert(!trimButton.IsEnabled, "Rapid selection changes leave stale caches disabled");
                        await window.Controller.LoadFolderAsync(audioFolder);
                    }
                    finally { Directory.Delete(resultFolder, true); }

                    var modeButton = (Button)window.FindName("PlaybackModeButton");
                    var modeImage = (Image)window.FindName("PlaybackModeImage");
                    Assert(modeButton.ToolTip?.ToString() == "单曲播放", "Playback mode defaults to Play One");
                    string[] modeLabels = { "单曲循环", "顺序播放", "列表循环", "单曲播放" };
                    PlaybackMode[] modeOrder = { PlaybackMode.RepeatOne, PlaybackMode.Sequential, PlaybackMode.RepeatAll, PlaybackMode.PlayOne };
                    for (int i = 0; i < modeOrder.Length; i++)
                    {
                        modeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert(modeButton.ToolTip?.ToString() == modeLabels[i]
                            && ReferenceEquals(modeImage.Source, window.FindResource(modeOrder[i] + "Icon"))
                            && settingsStore.LoadOrCreate().PlaybackMode == modeOrder[i],
                            "Mode cycles, updates icon and persists " + modeOrder[i]);
                    }
                    modeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var modeDialog = new SettingsWindow(settingsStore.LoadOrCreate());
                    var readModeValues = typeof(SettingsWindow).GetMethod("TryReadValues", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    object?[] modeArgs = { null };
                    Assert((bool)readModeValues.Invoke(modeDialog, modeArgs)! && ((AppSettings)modeArgs[0]!).PlaybackMode == PlaybackMode.RepeatOne,
                        "Normal settings validation preserves playback mode");
                    ((Button)modeDialog.FindName("RestoreDefaultsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert((bool)readModeValues.Invoke(modeDialog, modeArgs)! && ((AppSettings)modeArgs[0]!).PlaybackMode == PlaybackMode.PlayOne
                        && settingsStore.LoadOrCreate().PlaybackMode == PlaybackMode.RepeatOne,
                        "Restore Defaults stages Play One without saving until confirmation");
                    modeDialog.Close();
                    for (int i = 0; i < 3; i++) modeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    settingsButton.ApplyTemplate();
                    var settingsBorder = (Border)settingsButton.Template.FindName("ButtonBorder", settingsButton);
                    var settingsImage = FindVisual<Image>(settingsButton)!;
                    Assert(Math.Abs(settingsButton.Width - 36) < 0.01 && Math.Abs(settingsButton.Height - 36) < 0.01 &&
                           Math.Abs(settingsBorder.CornerRadius.TopLeft - 18) < 0.01, "Settings button is circular");
                    Assert(Math.Abs(settingsImage.Width - 28) < 0.01 && Math.Abs(settingsImage.Height - 28) < 0.01, "Settings icon is 28x28");
                    Assert(settingsBorder.BorderThickness == new Thickness(0) && ColorOf(settingsBorder.Background) == "#00FFFFFF",
                        "Settings button has no border or visible background");
                    var scriptButton = (Button)window.FindName("ScriptButton");
                    Assert(Grid.GetColumn(playButton) == 3 && Grid.GetColumn(stopButton) == 4 && Grid.GetColumn(trimButton) == 5 &&
                        Grid.GetColumn(refreshButton) == 6 && Grid.GetColumn(modeButton) == 7 &&
                        Grid.GetColumn(scriptButton) == 8 && Grid.GetColumn(settingsButton) == 9 &&
                        ((Grid)scriptButton.Parent).ColumnDefinitions.Count == 10,
                        "Toolbar order is play, stop, trim, refresh, mode, script, settings");
                    Assert(new[] { playButton, stopButton, trimButton, refreshButton, modeButton, scriptButton }
                        .All(button => Math.Abs(button.Margin.Right - 5) < 0.01) && settingsButton.Margin.Right == 0,
                        "Seven right toolbar buttons use 5-DIP gaps");
                    scriptButton.ApplyTemplate();
                    var scriptBorder = (Border)scriptButton.Template.FindName("ButtonBorder", scriptButton);
                    var scriptImage = FindVisual<Image>(scriptButton)!;
                    Assert(scriptButton.Width == 36 && scriptButton.Height == 36 &&
                        scriptBorder.CornerRadius.TopLeft == 18 && scriptBorder.BorderThickness == new Thickness(0),
                        "Script button uses existing circular toolbar style");
                    Assert(scriptImage.Width == 28 && scriptImage.Height == 28 &&
                        ReferenceEquals(scriptImage.Source, window.FindResource("ScriptIcon")),
                        "Script button uses its 28-DIP vector icon");
                    Assert(scriptButton.ToolTip?.ToString() == "显示原文" &&
                        System.Windows.Automation.AutomationProperties.GetName(scriptButton) == "显示原文",
                        "Script button initially offers to show the hidden companion");
                    var scriptDrawing = (DrawingGroup)((DrawingImage)window.FindResource("ScriptIcon")).Drawing;
                    var scriptBackground = (GeometryDrawing)scriptDrawing.Children[1];
                    Assert(scriptBackground.Geometry is EllipseGeometry && ColorOf(scriptBackground.Brush) == "#FF26798A",
                        "Script icon uses the existing teal circle");
                    Assert(scriptDrawing.Children.Skip(2).OfType<GeometryDrawing>().Count() == 2 &&
                        scriptDrawing.Children.Skip(2).OfType<GeometryDrawing>()
                            .All(part => part.Pen is not null && ColorOf(part.Pen.Brush) == "#FFFFFFFF"),
                        "Script icon uses white document outline and text lines");
                    Assert(Math.Abs(slider.Height - 36) < 0.01, "Progress slider has the same control height as the buttons");
                    window.UpdateLayout();
                    var sliderTrack = FindVisual<System.Windows.Controls.Primitives.Track>(slider);
                    double buttonCenterY = playButton.TranslatePoint(new Point(0, playButton.ActualHeight / 2), window).Y;
                    double sliderCenterY = slider.TranslatePoint(new Point(0, slider.ActualHeight / 2), window).Y;
                    Assert(slider.RenderTransform is TranslateTransform { Y: 10 },
                        "Progress slider has a 10-DIP downward visual offset");
                    Assert(Math.Abs(sliderCenterY - buttonCenterY - 10) < 1.5,
                        "Progress slider center is 10 DIP below the button centers");
                    Assert(sliderTrack is not null, "Progress slider exposes a visible WPF track");
                    Assert(settingsButton.ToolTip?.ToString() == "设置", "Settings gear has the updated name");
                    Assert(settingsButton.HorizontalAlignment == HorizontalAlignment.Center && settingsButton.VerticalAlignment == VerticalAlignment.Center, "Settings button is centered in the top row");
                    Assert(settingsButton.FocusVisualStyle is null, "Settings button has no focus visual");
                    foreach (string key in new[] { "OpenFolderIcon", "PlayIcon", "PauseIcon", "StopIcon", "TrimIcon", "RefreshIcon", "SettingsIcon", "PlayOneIcon", "RepeatOneIcon", "SequentialIcon", "RepeatAllIcon" })
                    {
                        var drawing = (DrawingGroup)((DrawingImage)window.FindResource(key)).Drawing;
                        var background = (GeometryDrawing)drawing.Children[1];
                        var symbol = (DrawingGroup)drawing.Children[2];
                        Assert(background.Geometry is EllipseGeometry && ColorOf(background.Brush) == "#FF26798A",
                            key + " uses the same teal circular background");
                        Assert(symbol.Children.OfType<GeometryDrawing>().All(part => ColorOf(part.Brush) == "#FFFFFFFF"),
                            key + " has a uniform white foreground");
                    }
                    var playDrawing = (DrawingGroup)((DrawingImage)window.FindResource("PlayIcon")).Drawing;
                    var playSymbol = (DrawingGroup)playDrawing.Children[2];
                    var playMatrix = (MatrixTransform)playSymbol.Transform;
                    Assert(Math.Abs(playMatrix.Matrix.OffsetX - (-35.80336528)) < 0.01,
                        "Play triangle moves right inside the unchanged circular background");

                    var stopDrawing = (DrawingGroup)((DrawingImage)window.FindResource("StopIcon")).Drawing;
                    var stopCircle = (EllipseGeometry)((GeometryDrawing)stopDrawing.Children[1]).Geometry;
                    var stopSymbol = (DrawingGroup)stopDrawing.Children[2];
                    var stopMatrix = (MatrixTransform)stopSymbol.Transform;
                    var stopSquare = (RectangleGeometry)((GeometryDrawing)stopSymbol.Children[0]).Geometry;
                    Rect stopBounds = stopMatrix.TransformBounds(stopSquare.Bounds);
                    Assert(Math.Abs(stopBounds.Height / (2 * stopCircle.RadiusY) - 0.35) < 0.001
                        && Math.Abs(stopBounds.Left + stopBounds.Width / 2 - stopCircle.Center.X) < 0.01
                        && Math.Abs(stopBounds.Top + stopBounds.Height / 2 - stopCircle.Center.Y) < 0.01,
                        "Stop square is centered and exactly 35% of circle height");

                    var settingsWindow = new SettingsWindow(new AppSettings());
                    var settingsTabs = (TabControl)settingsWindow.FindName("SettingsTabs");
                    Assert(settingsTabs.Items.Count == 2 && (string)((TabItem)settingsTabs.Items[0]).Header == "音频设置" &&
                        (string)((TabItem)settingsTabs.Items[1]).Header == "字幕参数" && settingsTabs.BorderThickness == new Thickness(0),
                        "Settings tabs have the agreed names and no outer border");
                    Assert(((ComboBox)settingsWindow.FindName("FontBox")).Items.Cast<string>().SequenceEqual(new[] { "Newsreader", "Source Serif 4", "Georgia", "Microsoft YaHei UI", "Nunito" }) &&
                        ((ComboBox)settingsWindow.FindName("FontBox")).SelectedIndex == 0 &&
                        ((TextBox)settingsWindow.FindName("FontSizeBox")).Text == "17" &&
                        ((TextBox)settingsWindow.FindName("LineHeightBox")).Text == "1.6" &&
                        ((TextBox)settingsWindow.FindName("ParagraphAfterBox")).Text == "16" &&
                        ((TextBox)settingsWindow.FindName("IndentBox")).Text == "2",
                        "Script font choices and defaults are Newsreader, 17, 1.6, 16 and 2");
                    Assert(settingsWindow.Title == "设置", "Settings dialog title has the updated name");
                    Assert(Math.Abs(settingsWindow.Width - 480) < 0.01, "Settings window width is 480");
                    Assert(Math.Abs(settingsWindow.Height - 430) < 0.01, "Settings window height is 430");
                    foreach (string textBoxName in new[] { "FrameMillisecondsTextBox", "MinimumSilenceTextBox", "SilenceThresholdTextBox", "NavigationToleranceTextBox", "AllowedSilenceTextBox", "BackwardStepTextBox", "ForwardStepTextBox" })
                    {
                        var textBox = (TextBox)settingsWindow.FindName(textBoxName);
                        Assert(textBox.TextAlignment == TextAlignment.Center && textBox.VerticalContentAlignment == VerticalAlignment.Center && !textBox.AllowDrop && textBox.FontWeight == FontWeights.Normal, textBoxName + " retains input alignment and normal weight");
                        textBox.ApplyTemplate();
                        Assert(FindVisual<Border>(textBox)!.CornerRadius.TopLeft == 6, "Input has rounded corners");
                    }
                    var excludedBox = (TextBox)settingsWindow.FindName("ExcludedFolderNamesTextBox");
                     Assert(excludedBox.Text == "" && excludedBox.FontWeight == FontWeights.Normal && excludedBox.TextAlignment == TextAlignment.Left && Grid.GetRow(excludedBox) == 6, "Exclusion field defaults empty below explanatory text");
                     var help = (TextBlock)settingsWindow.FindName("HelpText");
                     var exclusionLabel = (TextBlock)settingsWindow.FindName("ExcludedFolderNamesLabel");
                     Assert(help.Text == "允许静音时长只影响裁剪，不影响左右跳转。", "Only essential help text remains");
                     settingsWindow.ShowInTaskbar = false;
                     settingsWindow.ShowActivated = false;
                     settingsWindow.Left = -20000;
                     settingsWindow.Top = -20000;
                     settingsWindow.Show();
                     settingsWindow.UpdateLayout();
                     Rect Bounds(FrameworkElement element) => element.TransformToAncestor(settingsWindow)
                         .TransformBounds(new Rect(new Point(), element.RenderSize));
                     var audioParameterInputs = new[] { "FrameMillisecondsTextBox", "MinimumSilenceTextBox",
                         "SilenceThresholdTextBox", "NavigationToleranceTextBox", "AllowedSilenceTextBox",
                         "BackwardStepTextBox", "ForwardStepTextBox" }
                         .Select(name => (TextBox)settingsWindow.FindName(name)).ToArray();
                     var audioParameterGrid = (Grid)audioParameterInputs[0].Parent;
                     var audioParameterBounds = audioParameterInputs.Select(Bounds).ToArray();
                     var audioParameterRows = audioParameterGrid.RowDefinitions.Take(5).Select(row => row.ActualHeight).ToArray();
                     Assert(audioParameterRows.All(height => Math.Abs(height - 43) <= 0.5) &&
                         audioParameterBounds.All(bounds => Math.Abs(bounds.Height - 30) <= 0.5 &&
                             Math.Abs(bounds.Width - 80) <= 0.5) && Math.Abs(excludedBox.ActualHeight - 30) <= 0.5,
                         "Audio parameter rows are 43 DIP and input boxes are 30 DIP high");
                     Assert(audioParameterInputs.Select((box, index) =>
                         Math.Abs(audioParameterBounds[index].Top - Bounds(audioParameterGrid).Top -
                             Grid.GetRow(box) * 43 - 6.5) <= 0.5).All(centered => centered),
                         "Audio inputs are vertically centered in their parameter rows");
                     foreach (var input in audioParameterInputs.Append(excludedBox))
                     {
                         var host = (ScrollViewer)input.Template.FindName("PART_ContentHost", input);
                         var line = new TextBlock { Text = input.Text.Length == 0 ? "audio" : input.Text,
                             FontFamily = input.FontFamily, FontSize = input.FontSize, FontWeight = input.FontWeight };
                         line.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                         Assert(input.Padding == new Thickness(6, 1, 6, 1) && host.Margin == new Thickness(0) &&
                             host.ViewportHeight >= line.DesiredSize.Height - 0.5 &&
                             host.ExtentHeight <= host.ViewportHeight + 0.5,
                             input.Name + " has enough internal viewport height for a full text line");
                     }
                     Rect helpBounds = Bounds(help);
                     Rect exclusionBounds = Bounds(excludedBox);
                     Rect labelBounds = Bounds(exclusionLabel);
                     Rect buttonsBounds = Bounds((Button)settingsWindow.FindName("OkButton"));
                     Assert(helpBounds.Height > 0 && exclusionBounds.Height > 0 && labelBounds.Height > 0
                         && helpBounds.Bottom <= Math.Min(exclusionBounds.Top, labelBounds.Top)
                         && labelBounds.Right <= exclusionBounds.Left
                         && Math.Max(exclusionBounds.Bottom, labelBounds.Bottom) <= buttonsBounds.Top
                         && buttonsBounds.Bottom <= settingsWindow.ActualHeight + 0.5,
                         "Settings help, exclusion controls and buttons do not overlap or overflow");
                     settingsTabs.SelectedIndex = 1;
                     settingsWindow.UpdateLayout();
                     var fontSelector = (ComboBox)settingsWindow.FindName("FontBox");
                     var subtitleInputs = new[] { "FontSizeBox", "LineHeightBox", "ParagraphAfterBox", "IndentBox" }
                         .Select(name => (TextBox)settingsWindow.FindName(name)).ToArray();
                     var selectors = new FrameworkElement[] { fontSelector, subtitleInputs[0], subtitleInputs[1], subtitleInputs[2], subtitleInputs[3] };
                     Assert(fontSelector.Items.Count == 5 && fontSelector.SelectedItem is not null &&
                         fontSelector.FontWeight == FontWeights.Normal && fontSelector.HorizontalContentAlignment == HorizontalAlignment.Center &&
                         subtitleInputs.Select(box => box.MaxLength).SequenceEqual(new[] { 2, 4, 2, 1 }) &&
                         subtitleInputs.All(box => box.TextAlignment == TextAlignment.Center && box.VerticalContentAlignment == VerticalAlignment.Center &&
                             box.FontWeight == FontWeights.Normal && !box.AllowDrop) &&
                         selectors.All(box => box.Margin == new Thickness(0) && Math.Abs(box.ActualHeight - 30) <= 0.5 &&
                             Bounds(box).Bottom <= buttonsBounds.Top),
                         "Script font selector and four numeric text boxes are aligned and fit above dialog buttons");
                     var subtitleGrid = (Grid)selectors[0].Parent;
                     var columns = subtitleGrid.ColumnDefinitions;
                     double leftWidth = columns[0].ActualWidth + columns[1].ActualWidth;
                     double rightWidth = columns[3].ActualWidth + columns[4].ActualWidth;
                     var fontBounds = Bounds(selectors[0]);
                     var fontLabel = subtitleGrid.Children.OfType<TextBlock>().Single(label => label.Text == "字体");
                     Assert(Math.Abs(leftWidth - rightWidth) <= 1 && Math.Abs(columns[2].ActualWidth - 12) <= 0.5 &&
                         selectors.Skip(1).All(box => Math.Abs(box.ActualWidth - audioParameterBounds[0].Width) <= 0.5) &&
                         Math.Abs(fontBounds.Width - 160) <= 0.5 && Bounds(fontLabel).Right <= fontBounds.Left + 0.5 &&
                         Math.Abs(fontBounds.Right - Bounds(selectors[2]).Right) <= 0.5,
                         "Subtitle columns split available width equally; numeric selectors align and font selector is wider");
                     Assert(subtitleGrid.RowDefinitions.Take(3).Select((row, index) =>
                         Math.Abs(row.ActualHeight - audioParameterRows[index]) <= 0.5).All(matches => matches) &&
                         selectors.All(box => Math.Abs(box.ActualHeight - audioParameterBounds[0].Height) <= 0.5 &&
                             Math.Abs(Bounds(box).Top - audioParameterBounds[Grid.GetRow(box)].Top) <= 0.5),
                         "Subtitle and audio parameter rows share the same height, spacing and vertical positions");
                     foreach (string font in fontSelector.Items)
                     {
                         var text = new TextBlock { Text = font, FontFamily = fontSelector.FontFamily,
                             FontSize = fontSelector.FontSize, FontWeight = fontSelector.FontWeight };
                         text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                         Assert(text.DesiredSize.Width <= fontBounds.Width - 30,
                             "Font selector has space for " + font);
                     }
                     {
                         var box = fontSelector;
                         box.ApplyTemplate();
                         var toggle = FindVisual<System.Windows.Controls.Primitives.ToggleButton>(box)!;
                         toggle.ApplyTemplate();
                         var presenter = (ContentPresenter)box.Template.FindName("SelectedValue", box);
                         var line = new TextBlock { Text = box.SelectedItem!.ToString(), FontFamily = box.FontFamily,
                             FontSize = box.FontSize, FontWeight = box.FontWeight };
                         line.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                         Assert(presenter.ActualHeight >= line.DesiredSize.Height - 0.5 &&
                             Bounds(presenter).Top >= Bounds(box).Top && Bounds(presenter).Bottom <= Bounds(box).Bottom + 0.5, "Subtitle selector has space for a full text line");
                         Assert(((Border)toggle.Template.FindName("Outline", toggle)).CornerRadius.TopLeft == 6,
                             "Script combo box shares rounded input styling");
                         var option = new ComboBoxItem { Style = box.ItemContainerStyle, Content = box.SelectedItem };
                         option.ApplyTemplate();
                         var surface = (Border)option.Template.FindName("OptionSurface", option);
                         Assert(surface.CornerRadius.TopLeft == 4 && surface.BorderThickness == new Thickness(0) &&
                             ColorOf(surface.Background) == "#00FFFFFF" && option.FocusVisualStyle is null &&
                             option.Template.Triggers.OfType<Trigger>().Any(trigger =>
                                 trigger.Property == ComboBoxItem.IsHighlightedProperty),
                             "Dropdown item uses borderless rounded highlight styling");
                         option.IsSelected = true;
                         Assert(ColorOf(surface.Background) == "#FFD9EBEF" && ColorOf(option.Foreground) == "#FF26798A",
                             "Dropdown selected item uses the settings theme");
                     }
                     foreach (var input in subtitleInputs)
                     {
                         input.ApplyTemplate();
                         var host = (ScrollViewer)input.Template.FindName("PART_ContentHost", input);
                         var line = new TextBlock { Text = input.Text, FontFamily = input.FontFamily, FontSize = input.FontSize };
                         line.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                         Assert(FindVisual<Border>(input)!.CornerRadius.TopLeft == 6 &&
                             host.ViewportHeight >= line.DesiredSize.Height - 0.5 &&
                             host.ExtentHeight <= host.ViewportHeight + 0.5, input.Name + " has a full text viewport");
                     }
                     var colorLists = new[] { "TextColorBox", "PlaybackColorBox", "BackgroundColorBox" }
                         .Select(name => (ListBox)settingsWindow.FindName(name)).ToArray();
                     Assert(settingsWindow.FindName("AppearancePreview") is null, "Settings text preview removed");
                     var textBounds = Bounds(colorLists[0]);
                     var playbackBounds = Bounds(colorLists[1]);
                     var backgroundBounds = Bounds(colorLists[2]);
                     var gridBounds = Bounds(subtitleGrid);
                     Assert(Math.Abs(subtitleGrid.RowDefinitions[4].ActualHeight - 43) <= 0.5 &&
                         Math.Abs(subtitleGrid.RowDefinitions[5].ActualHeight - 43) <= 0.5 &&
                         Math.Abs(backgroundBounds.Top - textBounds.Top - 43) <= 0.5 &&
                         Math.Abs(textBounds.Top - playbackBounds.Top) <= 0.5 &&
                         Math.Abs(textBounds.Left - backgroundBounds.Left) <= 0.5 &&
                         backgroundBounds.Top >= textBounds.Bottom &&
                         textBounds.Right <= gridBounds.Left + leftWidth + 0.5 &&
                         playbackBounds.Left >= gridBounds.Left + leftWidth + columns[2].ActualWidth - 0.5 &&
                         playbackBounds.Right <= gridBounds.Right + 0.5 &&
                         Math.Abs(textBounds.Right - fontBounds.Right) <= 0.5 &&
                         Math.Abs(playbackBounds.Right - Bounds(selectors[1]).Right) <= 0.5,
                         "Text and playback palettes share a row in separate halves; background stays below text");
                     foreach (var colors in colorLists)
                     {
                         var label = subtitleGrid.Children.OfType<TextBlock>().Single(item =>
                             Grid.GetRow(item) == Grid.GetRow(colors) && Grid.GetColumn(item) == Grid.GetColumn(colors));
                         Assert(Bounds(label).Right <= Bounds(colors).Left + 0.5 &&
                             Math.Abs(colors.ActualWidth - 140) <= 0.5,
                             "Palette label and all five compact swatches fit within their half");
                     }
                     foreach (var colors in colorLists)
                     {
                         Assert(colors.Items.Count == 5 && colors.SelectedIndex == 0 &&
                             colors.SelectionMode == SelectionMode.Single &&
                             Bounds(colors).Bottom <= buttonsBounds.Top, "Five color choices fit above dialog buttons");
                         for (int i = 0; i < colors.Items.Count; i++)
                         {
                             var item = (ListBoxItem)colors.ItemContainerGenerator.ContainerFromIndex(i);
                             item.ApplyTemplate();
                             var swatch = (System.Windows.Shapes.Ellipse)item.Template.FindName("Swatch", item);
                             var tick = (System.Windows.Shapes.Path)item.Template.FindName("Check", item);
                             Assert(item.Width == 28 && item.Height == 28 && swatch.Width == 24 && swatch.Height == 24 && swatch.StrokeThickness == 0 &&
                                 Bounds(swatch).Right <= Bounds(colors).Right + 0.5 &&
                                 ColorOf(swatch.Fill) == "#FF" + ((string)colors.Items[i])[1..] &&
                                 (tick.Visibility == Visibility.Visible) == (i == colors.SelectedIndex) &&
                                 (colors != colorLists[1] || ColorOf(tick.Stroke) == "#FF283747"),
                                 "Color swatch is borderless and only the selected item shows a check");
                         }
                         colors.SelectedIndex = 2;
                         colors.SelectedIndex = -1;
                         Assert(colors.SelectedIndex == 2, "A color group cannot become unselected");
                     }
                     var readAppearance = typeof(SettingsWindow).GetMethod("ReadAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!;
                     var chosenAppearance = (ScriptAppearance)readAppearance.Invoke(settingsWindow, null)!;
                     Assert(chosenAppearance.Color == (string)colorLists[0].Items[2] &&
                         chosenAppearance.ScriptColor == (string)colorLists[1].Items[2] &&
                         chosenAppearance.Background == (string)colorLists[2].Items[2] &&
                         settingsWindow.Settings.ScriptAppearance == new ScriptAppearance(),
                         "Color choices remain staged until confirmation");
                     ((ComboBox)settingsWindow.FindName("FontBox")).SelectedItem = "Source Serif 4";
                     subtitleInputs[0].Text = "40";
                     subtitleInputs[1].Text = "2.75";
                     subtitleInputs[2].Text = "0";
                     subtitleInputs[3].Text = "5";
                     ((Button)settingsWindow.FindName("RestoreDefaultsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                     Assert((ScriptAppearance)readAppearance.Invoke(settingsWindow, null)! == new ScriptAppearance() &&
                         colorLists.All(list => list.SelectedIndex == 0), "Restore defaults resets all subtitle controls");
                     settingsTabs.SelectedIndex = 0;
                     settingsWindow.UpdateLayout();
                     settingsWindow.Hide();
                     var backwardBox = (TextBox)settingsWindow.FindName("BackwardStepTextBox");
                     var forwardBox = (TextBox)settingsWindow.FindName("ForwardStepTextBox");
                     Assert(backwardBox.Text == "5" && forwardBox.Text == "4" && backwardBox.MaxLength == 2 && forwardBox.MaxLength == 2, "Step defaults and length");
                     MethodInfo stepInput = typeof(SettingsWindow).GetMethod("IsStepText", BindingFlags.Static | BindingFlags.NonPublic)!;
                     Assert(new[] { "", "0", "99", "05" }.All(v => (bool)stepInput.Invoke(null, new object[] { v })!)
                         && new[] { "+5", "-1", "100", "2.5", "５", "a" }.All(v => !(bool)stepInput.Invoke(null, new object[] { v })!), "Step input is ASCII digits only");
                     Assert(MainWindow.IsPlaybackShortcut(Key.Left, ModifierKeys.Control) && MainWindow.IsPlaybackShortcut(Key.Right, ModifierKeys.Control)
                         && MainWindow.IsPlaybackShortcut(Key.Space, ModifierKeys.None) && !MainWindow.IsPlaybackShortcut(Key.Space, ModifierKeys.Control)
                         && !MainWindow.IsPlaybackShortcut(Key.Left, ModifierKeys.Control | ModifierKeys.Shift), "Shortcut modifier routing");
                     MethodInfo integerInput = typeof(SettingsWindow).GetMethod("IsIntegerText", BindingFlags.Static | BindingFlags.NonPublic)!;
                     Assert(new[] { "", "+", "+123", "0", "123" }.All(v => (bool)integerInput.Invoke(null, new object[] { v, false })!)
                         && new[] { "-", "-1", "1.5", "abc", "５", "1+2" }.All(v => !(bool)integerInput.Invoke(null, new object[] { v, false })!),
                         "Positive parameter boxes accept only digits and optional leading plus sign");
                     Assert(new[] { "", "+", "-", "-45", "+45", "0" }.All(v => (bool)integerInput.Invoke(null, new object[] { v, true })!)
                         && new[] { "4-5", "-4.5", "--1", "５", "a" }.All(v => !(bool)integerInput.Invoke(null, new object[] { v, true })!),
                         "Silence threshold box accepts digits and one leading sign only");
                    MethodInfo editableSeconds = typeof(SettingsWindow).GetMethod("IsEditableSilenceSeconds", BindingFlags.Static | BindingFlags.NonPublic)!;
                     MethodInfo completeSeconds = typeof(SettingsWindow).GetMethod("IsCompleteSilenceSeconds", BindingFlags.Static | BindingFlags.NonPublic)!;
                     Assert((bool)editableSeconds.Invoke(null, new object[] { "2." })!
                          && (bool)editableSeconds.Invoke(null, new object[] { "" })!
                          && !(bool)editableSeconds.Invoke(null, new object[] { "2.35" })!
                          && !(bool)editableSeconds.Invoke(null, new object[] { "2.00" })!,
                          "Seconds input limits typed and pasted precision");
                     Assert((bool)completeSeconds.Invoke(null, new object[] { "2." })!
                          && (bool)completeSeconds.Invoke(null, new object[] { "2.0" })!
                          && !(bool)completeSeconds.Invoke(null, new object[] { "." })!,
                          "Trailing period is valid at confirmation");
                     var restoreButton = (Button)settingsWindow.FindName("RestoreDefaultsButton");
                     var cancelButton = (Button)settingsWindow.FindName("CancelButton");
                     var okButton = (Button)settingsWindow.FindName("OkButton");
                     Assert(Math.Abs(restoreButton.Width - 76) < 0.01, "Restore button width is 76");
                     Assert(Math.Abs(cancelButton.Width - 54) < 0.01 && Math.Abs(okButton.Width - 54) < 0.01, "Dialog button widths are 54");
                     var allowedBox = (TextBox)settingsWindow.FindName("AllowedSilenceTextBox");
                     MethodInfo readValues = typeof(SettingsWindow).GetMethod("TryReadValues",
                         BindingFlags.Instance | BindingFlags.NonPublic)!;
                     MethodInfo lineHeightInput = typeof(SettingsWindow).GetMethod("IsLineHeightText", BindingFlags.Static | BindingFlags.NonPublic)!;
                     Assert(new[] { "", ".", "1", "1.", "1.6", "1.65", "3.00" }.All(v => (bool)lineHeightInput.Invoke(null, new object[] { v })!) &&
                         new[] { "+1", "-1", "1.625", "1..2", "１", "1,2", " 1", "1\n" }.All(v => !(bool)lineHeightInput.Invoke(null, new object[] { v })!),
                         "Line height input permits only ASCII digits, one point and at most two decimals");
                     foreach (var (input, accepted, rejected) in new[]
                     {
                         (subtitleInputs[0], "28", "+2"), (subtitleInputs[1], "1.65", "1.625"),
                         (subtitleInputs[2], "40", "４０"), (subtitleInputs[3], "5", "12")
                     })
                     {
                         foreach (var (candidate, valid) in new[] { (accepted, true), (rejected, false) })
                         {
                             input.SelectAll();
                             var typing = new TextCompositionEventArgs(Keyboard.PrimaryDevice,
                                 new TextComposition(InputManager.Current, input, candidate))
                                 { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
                             input.RaiseEvent(typing);
                             var data = new DataObject(DataFormats.Text, candidate);
                             var paste = new DataObjectPastingEventArgs(data, false, DataFormats.Text);
                             input.RaiseEvent(paste);
                             Assert(typing.Handled == !valid && paste.CommandCancelled == !valid,
                                 input.Name + " applies the same input grammar to typing and selection replacement by paste");
                         }
                     }
                     var subtitleToasts = (ToastHost)settingsWindow.FindName("ToastMessages");
                     string[] invalidValues = { "41", "3.01", "41", "6" };
                     string[] validValues = { "28", "1.65", "0", "5" };
                     for (int i = 0; i < subtitleInputs.Length; i++)
                     {
                         var input = subtitleInputs[i];
                         input.Text = invalidValues[i];
                         Assert(!(bool)readValues.Invoke(settingsWindow, new object?[] { null })! &&
                             input.BorderBrush == Brushes.IndianRed, input.Name + " rejects out of range value");
                         input.Text = "";
                         Assert(!(bool)readValues.Invoke(settingsWindow, new object?[] { null })!, input.Name + " rejects empty value");
                         input.Text = validValues[i];
                         Assert(input.BorderBrush != Brushes.IndianRed &&
                             (bool)readValues.Invoke(settingsWindow, new object?[] { null })!, input.Name + " accepts corrected value");
                         subtitleToasts.Clear();
                     }
                     subtitleInputs[1].Text = "1.60";
                     Assert((bool)readValues.Invoke(settingsWindow, new object?[] { null })! && subtitleInputs[1].Text == "1.6",
                         "Line height normalizes trailing zeroes without rounding");
                     restoreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                     excludedBox.Text = " audio , video; AUDIO；中文 文件夹，";
                     Assert((bool)readValues.Invoke(settingsWindow, new object?[] { null })! && excludedBox.Text == "audio, video, 中文 文件夹", "Folder names normalize on validation");
                     excludedBox.Text = "audio*";
                     Assert(!(bool)readValues.Invoke(settingsWindow, new object?[] { null })!, "Invalid folder name prevents confirmation");
                     var exclusionToasts = (ToastHost)settingsWindow.FindName("ToastMessages");
                     Assert(excludedBox.BorderBrush == Brushes.IndianRed
                         && FindVisual<StackPanel>(exclusionToasts)!.Children.Count == 1,
                         "Invalid folder name shows one warning");
                     excludedBox.Text = "";
                     Assert(excludedBox.BorderBrush != Brushes.IndianRed, "Correcting folder name clears red border");
                     exclusionToasts.Clear(); // Isolate the next independent validation scenario.
                     allowedBox.Text = "2.";
                     Assert((bool)readValues.Invoke(settingsWindow, new object?[] { null })!
                         && allowedBox.Text == "2.0", "Confirming 2. normalizes it to 2.0");
                     allowedBox.Text = "";
                     var modalToasts = (ToastHost)settingsWindow.FindName("ToastMessages");
                     var modalToastStack = FindVisual<StackPanel>(modalToasts)!;
                     foreach (Button actionButton in new[] { okButton, cancelButton, restoreButton })
                     {
                         // Clicking an action changes keyboard focus before raising its Click event.
                         var focusLoss = new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice,
                             Environment.TickCount, allowedBox, actionButton)
                         { RoutedEvent = Keyboard.LostKeyboardFocusEvent };
                         allowedBox.RaiseEvent(focusLoss);
                     }
                     Assert(modalToastStack.Children.Count == 0,
                         "Dialog action focus changes do not validate or warn before the action");
                     okButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                     Assert(allowedBox.BorderBrush == Brushes.IndianRed
                         && settingsWindow.Settings.AllowedSilenceSeconds == 2.0m,
                         "Empty value keeps dialog open with a red border");
                     Assert(modalToastStack.Children.Count == 1,
                         "One invalid confirmation shows exactly one warning toast");
                     modalToasts.Clear();
                     var minimumBox = (TextBox)settingsWindow.FindName("MinimumSilenceTextBox");
                     var normalFocusLoss = new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice,
                         Environment.TickCount, allowedBox, minimumBox)
                     { RoutedEvent = Keyboard.LostKeyboardFocusEvent };
                     allowedBox.RaiseEvent(normalFocusLoss);
                     Assert(modalToastStack.Children.Count == 1,
                         "Moving between inputs still validates once on focus loss");
                     modalToasts.Clear();
                     ((TextBox)settingsWindow.FindName("MinimumSilenceTextBox")).Text = "350";
                     allowedBox.Text = "0.3";
                     okButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                     Assert(allowedBox.BorderBrush == Brushes.IndianRed,
                         "Minimum 350ms requires at least 0.4s");
                     allowedBox.Text = "0.4";
                     Assert(allowedBox.BorderBrush != Brushes.IndianRed,
                         "Correcting value clears red border");
                     restoreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                     Assert(allowedBox.Text == "2.0"
                         && FindVisual<StackPanel>(modalToasts)!.Children.Count == 0,
                         "Restore clears toast and resets precision");
                     settingsWindow.Close();

                    // Simulate an older canceled selection check that outlives the latest task.
                    var closingWindow = new MainWindow(
                        new SettingsStore(Path.Combine(audioFolder, "UiClosing.settings.json")))
                    { ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000 };
                    closingWindow.Show();
                    var pendingSelection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    Field<List<Task>>(closingWindow.Controller.Silence, "runningSelectionChecks")!.Add(pendingSelection.Task);
                    var closeCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    closingWindow.Closed += (_, _) => closeCompleted.TrySetResult();
                    closingWindow.Close();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert(!closeCompleted.Task.IsCompleted,
                        "Closing waits for an older pending selection check");
                    pendingSelection.TrySetResult();
                    await closeCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert(!closingWindow.IsVisible,
                        "Closing completes after all selection checks finish");

                    Assert(Math.Abs(window.FontSize - 12) < 0.01 && Math.Abs(timeText.FontSize - 12) < 0.01 && Math.Abs(list.FontSize - 12) < 0.01, "UI font size is 12");
                    Assert(Grid.GetColumn(slider) == 1 && Grid.GetColumn(timeText) == 2 && Grid.GetColumn(playButton) == 3, "Time display sits between progress slider and play button");
                    foreach (Button button in new[] { openButton, playButton, stopButton, trimButton, modeButton })
                    {
                        button.ApplyTemplate();
                        var border = (Border)button.Template.FindName("ButtonBorder", button);
                        var image = FindVisual<Image>(button)!;
                        Assert(Math.Abs(button.Width - 36) < 0.01 && Math.Abs(button.Height - 36) < 0.01
                            && Math.Abs(border.CornerRadius.TopLeft - 18) < 0.01, button.Name + " has a 36x36 hit area");
                        Assert(border.BorderThickness == new Thickness(0) && ColorOf(border.Background) == "#00FFFFFF",
                            button.Name + " has no border or extra background");
                        Assert(Math.Abs(image.Width - 28) < 0.01 && Math.Abs(image.Height - 28) < 0.01,
                            button.Name + " icon is 28x28");
                        Assert(button.HorizontalContentAlignment == HorizontalAlignment.Center && button.VerticalContentAlignment == VerticalAlignment.Center, button.Name + " content is centered");
                        Assert(image.HorizontalAlignment == HorizontalAlignment.Center && image.VerticalAlignment == VerticalAlignment.Center, button.Name + " icon is horizontally and vertically centered");
                        Assert(button.VerticalAlignment == VerticalAlignment.Center, button.Name + " is vertically centered in the top row");
                        Assert(button.FocusVisualStyle is null, button.Name + " has no focus visual");
                        Assert(!button.Template.Triggers.OfType<Trigger>()
                            .SelectMany(trigger => trigger.Setters.OfType<Setter>())
                            .Any(setter => setter.Property == Border.BorderBrushProperty
                                || setter.Property == Border.BorderThicknessProperty),
                            button.Name + " never adds a hover or pressed border");
                    }
                    Assert(list.FocusVisualStyle is null && slider.FocusVisualStyle is null, "DataGrid and slider have no focus visual");
                    Assert(slider.VerticalAlignment == VerticalAlignment.Center && timeText.VerticalAlignment == VerticalAlignment.Center, "Progress slider and time display are vertically centered");
                    Assert(list.RowHeight == 25, "List row height is 25");
                    Assert(list.ColumnHeaderHeight == 32, "List header height is 32");
                    _ = window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var editWindow = app.Windows.OfType<SettingsWindow>().Single(w => w.IsVisible);
                        ((TextBox)editWindow.FindName("FrameMillisecondsTextBox")).Text = "25";
                        ((TextBox)editWindow.FindName("MinimumSilenceTextBox")).Text = "600";
                        ((ComboBox)editWindow.FindName("FontBox")).SelectedItem = "Source Serif 4";
                        ((TextBox)editWindow.FindName("FontSizeBox")).Text = "28";
                        ((TextBox)editWindow.FindName("LineHeightBox")).Text = "1.65";
                        ((TextBox)editWindow.FindName("ParagraphAfterBox")).Text = "0";
                        ((TextBox)editWindow.FindName("IndentBox")).Text = "5";
                        ((ListBox)editWindow.FindName("PlaybackColorBox")).SelectedIndex = 2;
                        ((Button)editWindow.FindName("OkButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }));
                    settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var saved = settingsStore.LoadOrCreate();
                    Assert(saved.FrameMilliseconds == 25 && saved.MinimumSilenceMilliseconds == 600
                        && saved.ScriptAppearance.Font == "Source Serif 4" && saved.ScriptAppearance.FontSize == 28 &&
                        saved.ScriptAppearance.LineHeight == 1.65 && saved.ScriptAppearance.ParagraphAfter == 0 &&
                        saved.ScriptAppearance.FirstLineIndent == 5 && saved.ScriptAppearance.ScriptColor == ScriptAppearance.PlaybackColors[2]
                        && window.Controller.Settings.ScriptAppearance == saved.ScriptAppearance
                        && window.Controller.Settings.FrameMilliseconds == 25,
                        "Settings dialog saves confirmed values before applying them");
                    string savedBeforeCancel = File.ReadAllText(settingsStore.FilePath);
                    _ = window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var editWindow = app.Windows.OfType<SettingsWindow>().Single(w => w.IsVisible);
                        ((ComboBox)editWindow.FindName("FontBox")).SelectedItem = "Georgia";
                        ((TextBox)editWindow.FindName("FontSizeBox")).Text = "18";
                        ((TextBox)editWindow.FindName("LineHeightBox")).Text = "2.05";
                        ((ListBox)editWindow.FindName("BackgroundColorBox")).SelectedIndex = 3;
                        editWindow.DialogResult = false;
                    }));
                    settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert(File.ReadAllText(settingsStore.FilePath) == savedBeforeCancel &&
                        window.Controller.Settings.ScriptAppearance == saved.ScriptAppearance,
                        "Cancel leaves persisted and active subtitle settings unchanged");
                    var restoredWindow = new MainWindow(settingsStore);
                    Assert(restoredWindow.Controller.Settings.FrameMilliseconds == 25 &&
                         restoredWindow.Controller.Settings.ScriptAppearance == saved.ScriptAppearance,
                        "Window restores saved settings on next launch");
                    restoredWindow.Close();
                    string invalidConfiguration = "{broken";
                    File.WriteAllText(settingsStore.FilePath, invalidConfiguration);
                    var invalidWindow = new MainWindow(settingsStore);
                    Assert(!invalidWindow.Controller.SettingsFileReady
                        && FindVisual<StackPanel>((ToastHost)invalidWindow.FindName("ToastMessages"))!.Children.Count > 0
                        && File.ReadAllText(settingsStore.FilePath) == invalidConfiguration,
                        "Invalid configuration is preserved and reported inline");
                    invalidWindow.Close();
                    settingsStore.Save(saved);
                    window.Controller.Session.BeginPreview(double.NaN);
                    window.Controller.Session.BeginPreview(-1);
                    Assert(!window.Controller.Session.PreviewTimerEnabled &&
                        window.Controller.Session.PreviewPosition is null,
                        "Invalid script navigation targets do not arm the debounce timer");
                    await window.Controller.Session.CommitPreviewAsync();
                    Assert(!window.Controller.Session.PreviewTimerEnabled,
                        "Commit without a pending script target is a no-op");
                    // Real WebView2 integration runs independently of audio hardware.
                    string? runtimeVersion = null;
                    try { runtimeVersion = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString(); }
                    catch (Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException) { }
                    if (string.IsNullOrWhiteSpace(runtimeVersion))
                        Console.WriteLine("SKIP UI script reader integration: WebView2 Runtime unavailable.");
                    else
                    {
                        string readerFolder = Path.Combine(Path.GetTempPath(), "script-reader-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(readerFolder);
                        var readerWindow = new ScriptWindow(Path.Combine(readerFolder, "cache"))
                            { Owner = window, Height = 400, ShowActivated = false, Left = -10000, Top = -10000 };
                        try
                        {
                            File.WriteAllText(Path.Combine(readerFolder, "arbitrary.script.json"),
                                """{"filetype":"script","title":"Reader test","paragraphs":[[{"text":"Hello, world.","timed_units":[["Hello",1,3],["world",0,2]]}]]}""");
                            readerWindow.SetAudio(Path.Combine(readerFolder, "sample.wav"));
                            readerWindow.Show();
                            // A cold WebView2 profile on a hosted Windows runner can take
                            // longer than a local startup. Keep a finite deadline and
                            // fail immediately when the reader reports an actual error.
                            var deadline = DateTime.UtcNow.AddSeconds(60);
                            while (!readerWindow.ReaderReady && readerWindow.ReaderFailure is null && DateTime.UtcNow < deadline)
                                await Task.Delay(100);
                            Assert(readerWindow.ReaderReady, readerWindow.ReaderReady
                                ? "Embedded WebView2 reader completes ready handshake"
                                : "Embedded WebView2 reader failed: " + (readerWindow.ReaderFailure ?? "no ready message within 60 seconds"));
                            var reader = (Microsoft.Web.WebView2.Wpf.WebView2)readerWindow.FindName("Reader");
                            string renderedScriptText = "";
                            deadline = DateTime.UtcNow.AddSeconds(25);
                            while (DateTime.UtcNow < deadline)
                            {
                                renderedScriptText = await reader.CoreWebView2.ExecuteScriptAsync("document.querySelector('#text').textContent");
                                if (renderedScriptText.Contains("Hello, world.")) break;
                                await Task.Delay(100);
                            }
                            Assert(renderedScriptText == "\"Hello, world.\"", "Embedded WebView2 reader renders Script JSON once without duplicate text");
                            string defaultFont = await reader.CoreWebView2.ExecuteScriptAsync("getComputedStyle(document.documentElement).fontFamily==='Newsreader, Georgia, serif'");
                            Assert(defaultFont == "true", "Reader default font is Newsreader with Georgia and generic serif fallback only");
                            readerWindow.SetAppearance(new ScriptAppearance
                                { Font = "Source Serif 4", FontSize = 28, LineHeight = 1.65, ParagraphAfter = 0, FirstLineIndent = 5 });
                            string newAppearance = "";
                            deadline = DateTime.UtcNow.AddSeconds(10);
                            while (DateTime.UtcNow < deadline)
                            {
                                newAppearance = await reader.CoreWebView2.ExecuteScriptAsync(
                                    """
                                    (() => {
                                      const root=document.documentElement, p=document.querySelector('#text p');
                                      const actual={fontFamily:getComputedStyle(root).fontFamily,
                                        fontSize:root.style.fontSize,lineHeight:root.style.lineHeight,
                                        paragraphAfter:getComputedStyle(p).marginBottom,indent:getComputedStyle(p).textIndent,
                                        measureFrame,units:geometry.length,anchors:followAnchors.length,
                                        validGeometry:geometry.every(u=>u.segments.length>0 && u.total>0)};
                                      return (actual.fontFamily==='"Source Serif 4", Georgia, serif' &&
                                        actual.fontSize==='28px' && actual.lineHeight==='1.65' &&
                                        actual.paragraphAfter==='0px' && actual.indent==='140px' &&
                                        actual.measureFrame===0 && actual.units===2 && actual.anchors>0 &&
                                        actual.validGeometry) || actual;
                                    })()
                                    """);
                                if (newAppearance == "true") break;
                                await Task.Delay(50);
                            }
                            Assert(newAppearance == "true", "Reader applies custom subtitle font and numeric values and recomputes geometry and anchors: " + newAppearance);
                            readerWindow.SetAppearance(new ScriptAppearance());
                            deadline = DateTime.UtcNow.AddSeconds(10);
                            string restoredAppearance = "";
                            while (DateTime.UtcNow < deadline)
                            {
                                restoredAppearance = await reader.CoreWebView2.ExecuteScriptAsync(
                                    """
                                    (() => {
                                      const root=document.documentElement, p=document.querySelector('#text p');
                                      const actual={fontFamily:getComputedStyle(root).fontFamily,
                                        fontSize:root.style.fontSize,lineHeight:root.style.lineHeight,
                                        paragraphAfter:getComputedStyle(p).marginBottom,indent:getComputedStyle(p).textIndent,
                                        measureFrame,units:geometry.length};
                                      return (actual.fontFamily==='Newsreader, Georgia, serif' &&
                                        actual.fontSize==='17px' && actual.lineHeight==='1.6' &&
                                        actual.paragraphAfter==='16px' && actual.indent==='34px' &&
                                        actual.measureFrame===0 && actual.units===2) || actual;
                                    })()
                                    """);
                                if (restoredAppearance == "true") break;
                                await Task.Delay(50);
                            }
                            Assert(restoredAppearance == "true", "Reader reapplies default appearance before playback regression checks: " + restoredAppearance);
                            string measured = await reader.CoreWebView2.ExecuteScriptAsync(
                                "measure(); update({position:1.5,playing:false,reset:false},true); geometry.length===2 && geometry[0].last===0.75 && geometry[1].last===0.25");
                            Assert(measured == "true", "Reader paints overlapping out-of-order intervals");
                            string backgroundPaint = await reader.CoreWebView2.ExecuteScriptAsync(
                                """
                                (() => {
                                  const p=body.querySelector('p'), layer=p.querySelector('svg.paint'), timed=p.querySelector('.timed');
                                  return p.textContent==='Hello, world.' && !p.querySelector('clipPath') &&
                                    getComputedStyle(p).isolation==='isolate' && getComputedStyle(layer).zIndex==='-1' &&
                                    getComputedStyle(layer).pointerEvents==='none' &&
                                    getComputedStyle(timed).color===getComputedStyle(p).color &&
                                    getComputedStyle(layer.querySelector('rect')).fill==='rgb(246, 237, 194)' &&
                                    getComputedStyle(layer.querySelector('rect')).shapeRendering.toLowerCase()==='crispedges' &&
                                    geometry.every(u=>u.segments.every(s=>Math.abs(s.element.width.baseVal.value-s.painted)<0.01));
                                })()
                                """);
                            Assert(backgroundPaint == "true", "Reader paints background rectangles beneath a single unchanged text layer");
                            string rewind = await reader.CoreWebView2.ExecuteScriptAsync(
                                "update({position:2.5,playing:false,reset:false},true); update({position:0.5,playing:false,reset:false},true); geometry[0].last===0.25 && geometry[1].segments.every(s=>s.painted===0)");
                            Assert(rewind == "true", "Reader rewinds previously painted intervals");
                            string cleared = await reader.CoreWebView2.ExecuteScriptAsync(
                                "update({position:0,playing:false,reset:true},true); geometry.every(u=>u.segments.every(s=>s.painted===0))");
                            Assert(cleared == "true", "Reader reset clears painted intervals");

                            string joinedBackground = await reader.CoreWebView2.ExecuteScriptAsync(
                                """
                                (() => {
                                  const saved=[...body.childNodes], savedPlayback=playback;
                                  const p=document.createElement('p'), wrap=document.createElement('p');
                                  const word=(text,start)=>'<span class="timed" data-start="'+start+'" data-end="'+(start+1)+'">'+text+'</span>';
                                  const sentence=text=>'<span class="sentence">'+text+'</span>';
                                  p.style.cssText='width:1000px;white-space:nowrap;text-indent:0';
                                  wrap.style.cssText='width:4em;text-indent:0';
                                  p.innerHTML=sentence(word('Alpha',0)+' '+word('beta',1))+' '+
                                    sentence(word('gamma',2)+' untimed '+word('delta',3)+', '+word('epsilon',4));
                                  wrap.innerHTML=sentence(word('Alpha',5)+' '+word('Beta',6));
                                  const layer=()=>{const svg=document.createElementNS(ns,'svg');svg.setAttribute('class','paint');return svg;};
                                  p.append(layer());wrap.append(layer());
                                  const near=(a,b)=>Math.abs(a-b)<0.02;
                                  try {
                                    body.replaceChildren(p,wrap);measure();
                                    if(geometry.length!==7 || geometry.some(u=>u.segments.length!==1))return false;
                                    const [a,b,c,d,e,f,g]=geometry.map(u=>u.segments[0]);
                                    const x=s=>s.element.x.baseVal.value;
                                    if(a.trailingGap<=0 || b.trailingGap<=0 ||
                                      !near(x(a)+a.width+a.trailingGap,x(b)) ||
                                      !near(x(b)+b.width+b.trailingGap,x(c)) ||
                                      [c,d,e,f,g].some(s=>s.trailingGap!==0) ||
                                      near(f.element.y.baseVal.value,g.element.y.baseVal.value))return false;
                                    paintAt(0.5,false);
                                    if(!near(a.painted,a.width/2) || b.painted!==0)return false;
                                    paintAt(1,false);
                                    if(!near(a.painted,a.width+a.trailingGap) || b.painted!==0)return false;
                                    paintAt(2.5,false);
                                    if(!near(b.painted,b.width+b.trailingGap) || !near(c.painted,c.width/2))return false;
                                    paintAt(7,false);
                                    if(body.querySelectorAll('svg.paint rect').length!==7 ||
                                      !near(c.painted,c.width) || !near(d.painted,d.width) ||
                                      !near(f.painted,f.width))return false;
                                    paintAt(0.5,false);
                                    if(!near(a.painted,a.width/2) || geometry.slice(1).some(u=>u.segments.some(s=>s.painted!==0)))return false;
                                    // Reflow the same words onto one line and recompute the bridge.
                                    wrap.style.width='1000px';measure();
                                    const [left,right]=geometry.slice(5).map(u=>u.segments[0]);
                                    if(left.trailingGap<=0 || !near(x(left)+left.width+left.trailingGap,x(right)))return false;
                                    // A single long timed unit wraps; only its last segment joins the next word.
                                    wrap.style.cssText='font-family:monospace;width:8ch;text-indent:0';
                                    wrap.innerHTML=sentence(word('ABCDEFGHIJK',5)+' '+word('xy',6));
                                    wrap.append(layer());measure();
                                    const unit=geometry[5], next=geometry[6].segments[0];
                                    if(unit.segments.length!==2 || geometry[6].segments.length!==1)return false;
                                    const [first,last]=unit.segments;
                                    if(first.trailingGap!==0 || last.trailingGap<=0 ||
                                      near(first.element.y.baseVal.value,last.element.y.baseVal.value) ||
                                      !near(last.element.y.baseVal.value,next.element.y.baseVal.value) ||
                                      !near(x(last)+last.width+last.trailingGap,x(next)))return false;
                                    paintAt(5+first.width/(2*unit.total),false);
                                    if(!near(first.painted,first.width/2) || last.painted!==0)return false;
                                    paintAt(5+(first.width+last.width/2)/unit.total,false);
                                    if(!near(first.painted,first.width) || !near(last.painted,last.width/2))return false;
                                    paintAt(6,false);
                                    if(!near(first.painted,first.width) || !near(last.painted,last.width+last.trailingGap) ||
                                      next.painted!==0 || body.querySelectorAll('svg.paint rect').length!==8)return false;
                                    paintAt(5.5,false);
                                    if(!near(first.painted,unit.total/2) || last.painted!==0 || next.painted!==0)return false;
                                    paintAt(7,false);paintAt(0,true);
                                    return geometry.every(u=>u.segments.every(s=>s.painted===0));
                                  } finally {
                                    body.replaceChildren(...saved);playback=savedPlayback;measure();
                                  }
                                })()
                                """);
                            Assert(joinedBackground == "true", "Reader joins same-line whitespace only, preserves word timing across wraps, leaves untimed text and punctuation gaps, and recomputes after reflow");

                            string continuousFollow = await reader.CoreWebView2.ExecuteScriptAsync(
                                """
                                (() => {
                                  const saved = followAnchors;
                                  try {
                                    followAnchors=buildFollowAnchors([
                                      {y:150,followStart:0},{y:250,followStart:2},
                                      {y:275,followStart:2},{y:290,followStart:1},
                                      {y:300,followStart:null},{y:350,followStart:4}
                                    ]);
                                    const offsets=[0,0.5,1,1.5,2,2.5,3,3.5,4]
                                      .map(t=>followOffset(t,100,1000));
                                    return followAnchors.length===3 &&
                                      offsets.every((x,i)=>x===100+25*i) &&
                                      offsets.every((x,i)=>timeAtOffset(x,100,1000)===i*0.5) &&
                                      timeAtOffset(126,100,1000)>0.5 &&
                                      timeAtOffset(0,100,1000)===0 &&
                                      timeAtOffset(900,100,1000)===4 &&
                                      timeAtOffset(0,100,80)===null &&
                                      followOffset(10,100,220)===120 &&
                                      followOffset(10,100,80)===0;
                                  } finally {followAnchors=saved;}
                                })()
                                """);
                            Assert(continuousFollow == "true", "Reader forward and inverse trajectories agree, including fine-grained scroll offsets and boundaries");
                            string followFrames = await reader.CoreWebView2.ExecuteScriptAsync(
                                """
                                (() => {
                                  const raf=window.requestAnimationFrame,caf=window.cancelAnimationFrame;
                                  const saved=followAnchors;
                                  let callback=null;
                                  try {
                                    window.requestAnimationFrame=cb=>{callback=cb;return 1;};
                                    window.cancelAnimationFrame=()=>{callback=null;};
                                    followAnchors=[{time:0,y:150},{time:2,y:250}];
                                    update({position:0.5,playing:true,reset:false},true);
                                    if(!callback || !animation)return false;
                                    callback(sampleAt+16);
                                    const a=followTime;
                                    callback(sampleAt+32);
                                    const b=followTime;
                                    callback(sampleAt+500);
                                    const capped=followTime;
                                    update({position:0.525,playing:true,reset:false});
                                    callback(sampleAt+16);
                                    const monotone=followTime>=capped;
                                    update({position:0.55,playing:false,reset:false});
                                    return b>a && Math.abs(capped-0.55)<0.00001 && monotone &&
                                      !callback && !animation;
                                  } finally {
                                    cancelAnimation();
                                    window.requestAnimationFrame=raf;
                                    window.cancelAnimationFrame=caf;
                                    followAnchors=saved;
                                    update({position:0,playing:false,reset:true},true);
                                  }
                                })()
                                """);
                            Assert(followFrames == "true", "Reader frame clock advances continuously, is bounded and cancels on pause");

                            string nativeWheel = await reader.CoreWebView2.ExecuteScriptAsync(
                                """
                                (() => {
                                  const originalScroll=window.scrollTo, originalPreview=preview, originalCancel=cancelAnimation;
                                  let calls=0;
                                  try {
                                    // Synthetic wheel events have no native scroll default action.
                                    // Verify the application does not intercept them or issue its own commands.
                                    window.scrollTo=preview=cancelAnimation=()=>{calls++;};
                                    return [0,1,2].every(deltaMode=>[120,-120].every(deltaY=>{
                                      const event=new WheelEvent('wheel',{deltaY,deltaMode,bubbles:true,cancelable:true});
                                      return body.querySelector('.sentence').dispatchEvent(event) &&
                                        !event.defaultPrevented && calls===0;
                                    }));
                                  } finally {
                                    window.scrollTo=originalScroll;preview=originalPreview;cancelAnimation=originalCancel;
                                  }
                                })()
                                """);
                            Assert(nativeWheel == "true" && !reader.CoreWebView2.Settings.IsZoomControlEnabled,
                                "Reader leaves wheel input native without direct scroll or playback commands and disables browser zoom control");

                            string scrollBarNavigation = await reader.CoreWebView2.ExecuteScriptAsync(
                                """
                                (() => {
                                  const originalPreview=preview, savedAnchors=followAnchors;
                                  const spacer=document.createElement('div');
                                  spacer.style.height='3000px';
                                  let targets=[];
                                  try {
                                    body.append(spacer);
                                    followAnchors=[{y:window.innerHeight/2+200,time:7},
                                                   {y:window.innerHeight/2+400,time:9}];
                                    preview=time=>targets.push(time);
                                    scrollProgrammatically(200);
                                    window.dispatchEvent(new Event('scroll'));
                                    if(targets.length!==0)return false;
                                    window.scrollTo(0,250);
                                    window.dispatchEvent(new Event('scroll'));
                                    if(targets.length!==1 || Math.abs(targets[0]-7.5)>0.02)return false;
                                    window.dispatchEvent(new Event('scroll'));
                                    if(targets.length!==1)return false;
                                    window.scrollTo(0,260);
                                    window.dispatchEvent(new Event('scroll'));
                                    if(targets.length!==2 || Math.abs(targets[1]-7.6)>0.02)return false;
                                    const sentence=body.querySelector('.sentence');
                                    sentence.dispatchEvent(new MouseEvent('dblclick',{bubbles:true,clientY:window.innerHeight/2}));
                                    if(targets.length!==2)return false;

                                    // Scrollable text without timed anchors must not start a seek.
                                    followAnchors=[];
                                    scrollProgrammatically(200);
                                    window.scrollTo(0,300);
                                    window.dispatchEvent(new Event('scroll'));
                                    if(targets.length!==2 || pending!==null || Math.abs(window.scrollY-300)>1)return false;

                                    // Exercise the real preview and follow-frame path, before the
                                    // browser delivers its queued external scroll event.
                                    preview=originalPreview;
                                    followAnchors=[{y:window.innerHeight/2+200,time:7},
                                                   {y:window.innerHeight/2+400,time:9}];
                                    update({position:7,playing:true,reset:false},true);
                                    if(!animation || pending!==null || Math.abs(window.scrollY-200)>1)return false;
                                    window.scrollTo(0,300);
                                    followFrame(sampleAt+16);
                                    if(animation || Math.abs((pending??-1)-8)>0.02 ||
                                       Math.abs(window.scrollY-300)>1 || playback.position!==7)return false;
                                    window.dispatchEvent(new Event('scroll'));
                                    return !animation && Math.abs((pending??-1)-8)<0.02 &&
                                      Math.abs(window.scrollY-300)<=1;
                                  } finally {
                                    preview=originalPreview;
                                    cancelManual();
                                    followAnchors=savedAnchors;
                                    spacer.remove();
                                    update({position:0,playing:false,reset:true},true);
                                  }
                                })()
                                """);
                            Assert(scrollBarNavigation == "true", "Reader external scroll previews continuous time and interrupts follow; programmatic scroll, double-click and untimed scroll do not seek");

                            string reconciliation = await reader.CoreWebView2.ExecuteScriptAsync(
                                """
                                (() => {
                                  const savedEpoch=epoch,savedAnchors=followAnchors;
                                  const spacer=document.createElement('div');
                                  spacer.style.height='3000px';
                                  try {
                                    body.append(spacer);
                                    followAnchors=[{y:window.innerHeight/2+200,time:1},
                                                   {y:window.innerHeight/2+400,time:3}];
                                    receivePlayback({epoch,position:1,playing:false,reset:false});
                                    pending=2;cancelAnimation();paintAt(2,false);scrollProgrammatically(300);
                                    if(playback.position!==1)return false;
                                    receivePlayback({epoch,position:1,playing:false,reset:false});
                                    if(pending!==2 || Math.abs(window.scrollY-300)>1)return false;
                                    receivePlayback({epoch:epoch+1,position:1,playing:false,reset:false});
                                    if(pending!==null || Math.abs(window.scrollY-200)>1 || paintPosition!==1)return false;
                                    receivePlayback({epoch:epoch-1,position:3,playing:false,reset:false});
                                    if(playback.position!==1)return false;
                                    pending=2;paintAt(2,false);scrollProgrammatically(310);
                                    receivePlayback({epoch:epoch+1,position:2,playing:false,reset:false,committedTarget:2});
                                    if(pending!==null || Math.abs(window.scrollY-310)>1)return false;
                                    pending=2.5;paintAt(2.5,false);scrollProgrammatically(350);
                                    receivePlayback({epoch:epoch+1,position:2,playing:false,reset:false,committedTarget:2});
                                    return pending===null && Math.abs(window.scrollY-300)<=1 && playback.position===2;
                                  } finally {
                                    cancelManual();epoch=savedEpoch;followAnchors=savedAnchors;
                                    spacer.remove();update({position:0,playing:false,reset:true},true);
                                  }
                                })()
                                """);
                            Assert(reconciliation == "true", "Reader preview never replaces host clock; reconciliation rejects stale snapshots and restores canceled or superseded offsets");

                            // Keep the synthetic article isolated from MainWindow's playback timer
                            // until the rendering assertions finish. No real audio is loaded here.
                            readerWindow.Hide();
                            nextReader = readerWindow;
                            var readerButton = (Button)window.FindName("ScriptButton");
                            readerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert(window.ScriptHost.HasWindow && window.ScriptHost.IsVisible &&
                                readerWindow.IsVisible && readerButton.ToolTip?.ToString() == "隐藏原文",
                                "Script host opens the reader and updates the button");
                            readerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert(window.ScriptHost.HasWindow && !window.ScriptHost.IsVisible &&
                                !readerWindow.IsVisible && readerButton.ToolTip?.ToString() == "显示原文",
                                "Script host hides the reader without destroying it");
                            readerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert(window.ScriptHost.IsVisible && readerWindow.IsVisible,
                                "Script host reuses the existing reader when shown again");
                            // Closing the companion itself also hides it and preserves the instance.
                            readerWindow.Close();
                            Assert(window.ScriptHost.HasWindow && !window.ScriptHost.IsVisible &&
                                readerButton.ToolTip?.ToString() == "显示原文",
                                "Companion close hides it and updates the owner");
                            readerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            var readerHandle = new System.Windows.Interop.WindowInteropHelper(readerWindow).Handle;
                            int readerStyle = GetWindowLong(readerHandle, -16);
                            Assert((readerStyle & 0x00030000) == 0 && (readerStyle & 0x00040000) != 0,
                                "Reader disables minimize and maximize while retaining resize border");
                            bool closedBeforeMinimize = false;
                            readerWindow.Closed += (_, _) => closedBeforeMinimize = window.WindowState != WindowState.Minimized;
                            SendMessage(new System.Windows.Interop.WindowInteropHelper(window).Handle, 0x0112, new IntPtr(0xf020), IntPtr.Zero);
                            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                            Assert(closedBeforeMinimize && window.WindowState == WindowState.Minimized && !IsWindow(readerHandle) &&
                                !window.ScriptHost.HasWindow,
                                "Native minimize command closes reader before minimizing owner");
                            window.WindowState = WindowState.Normal;
                            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                            Assert(!window.ScriptHost.HasWindow && !IsWindow(readerHandle),
                                "Restoring owner does not reopen closed reader");
                            var hiddenReader = new ScriptWindow(Path.Combine(readerFolder, "cache"));
                            try
                            {
                                nextReader = hiddenReader;
                                readerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                readerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                bool hiddenClosed = false;
                                hiddenReader.Closed += (_, _) => hiddenClosed = true;
                                window.WindowState = WindowState.Minimized;
                                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                                Assert(hiddenClosed && !window.ScriptHost.HasWindow,
                                    "Programmatic minimize closes even a hidden reader");
                                window.WindowState = WindowState.Normal;
                            }
                            finally { hiddenReader.CloseForOwner(); }
                        }
                        finally
                        {
                            nextReader = null;
                            // On an assertion failure, dispose through the owner host before
                            // releasing the standalone fixture.
                            if (window.ScriptHost.HasWindow)
                                window.WindowState = WindowState.Minimized;
                            window.WindowState = WindowState.Normal;
                            readerWindow.CloseForOwner();
                            for (int attempt = 0; attempt < 20; attempt++)
                            {
                                try { Directory.Delete(readerFolder, true); break; }
                                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                                {
                                    if (attempt == 19) Console.WriteLine("Temporary WebView2 cache still in use: " + readerFolder);
                                    else await Task.Delay(250);
                                }
                            }
                        }
                    }
                    if (WaveOut.DeviceCount == 0)
                    {
                        Console.WriteLine("SKIP UI script scroll playback checks (7 assertions plus Settings restoration): no waveOut device is available.");
                        Console.WriteLine("SKIP UI audio-device playback checks: no waveOut device is available.");
                        return;
                    }
                    string modeFolder = Path.Combine(audioFolder, "ui-modes-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(modeFolder);
                    try
                    {
                        string firstModePath = Path.Combine(modeFolder, "01.wav");
                        string secondModePath = Path.Combine(modeFolder, "02.wav");
                        using (var writer = new WaveFileWriter(firstModePath, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
                            writer.WriteSamples(new float[44100], 0, 44100);
                        File.Copy(firstModePath, secondModePath);
                        await window.Controller.LoadFolderAsync(modeFolder);
                        var modePlayer = Field<PlayerEngine>(window.Controller.Session, "player")!;
                        var session = window.Controller.Session;
                        async Task WaitModeAsync(Func<bool> condition)
                        {
                            var deadline = DateTime.UtcNow.AddSeconds(8);
                            while ((!condition() || window.Controller.Session.IsBusy) && DateTime.UtcNow < deadline) await Task.Delay(25);
                            Assert(condition() && !window.Controller.Session.IsBusy, "Playback mode transition completes");
                        }
                        async Task ClickModeStop()
                        {
                            stopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            await WaitModeAsync(() => !window.Controller.Session.IsBusy && !modePlayer.IsPlaying);
                        }
                        async Task StartModeTrack(int index)
                        {
                            // Use the normal play shortcut with the selected file on a fresh playlist.
                            if (modePlayer.IsLoaded && !ReferenceEquals(window.Controller.Session.LoadedTrack, list.Items[index]))
                                await window.Controller.LoadFolderAsync(modeFolder);
                            list.SelectedIndex = index;
                            await window.HandleShortcutAsync(Key.Space);
                        }
                        bool LoadedModeTrack(int index) => ReferenceEquals(window.Controller.Session.LoadedTrack, list.Items[index]);
                        await StartModeTrack(0);
                        await WaitModeAsync(() => !modePlayer.IsPlaying && modePlayer.GetCurrentTime() == modePlayer.GetDuration());
                        Assert(LoadedModeTrack(0), "Play One stops on current file");
                        modeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        int completions = 0;
                        EventHandler countCompletion = (_, _) => Interlocked.Increment(ref completions);
                        modePlayer.NaturalPlaybackEnded += countCompletion;
                        await StartModeTrack(0);
                        await WaitModeAsync(() => Volatile.Read(ref completions) >= 1 && modePlayer.IsPlaying && modePlayer.GetCurrentTime() < modePlayer.GetDuration());
                        Assert(LoadedModeTrack(0), "Repeat One restarts same file");
                        await ClickModeStop();
                        await Task.Delay(300);
                        Assert(!modePlayer.IsPlaying, "Manual Stop does not repeat");
                        modePlayer.NaturalPlaybackEnded -= countCompletion;
                        modeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await StartModeTrack(0);
                        await WaitModeAsync(() => LoadedModeTrack(1) && modePlayer.IsPlaying);
                        await WaitModeAsync(() => !modePlayer.IsPlaying && modePlayer.GetCurrentTime() == modePlayer.GetDuration());
                        Assert(LoadedModeTrack(1), "Sequential advances and stops at last file");
                        modeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await StartModeTrack(1);
                        await WaitModeAsync(() => LoadedModeTrack(0) && modePlayer.IsPlaying);
                        Assert(LoadedModeTrack(0), "Repeat All wraps to first file");
                        await window.HandleShortcutAsync(Key.Right, ModifierKeys.Control);
                        await Task.Delay(300);
                        Assert(LoadedModeTrack(0) && !modePlayer.IsPlaying, "Manual timed seek to end does not advance");

                        // Simulate the shared modal guard, then exercise actual Stop/seek routing.
                        session.ModalDialogOpen = true;
                        try
                        {
                            await modePlayer.PlayAsync();
                            await WaitModeAsync(() => modePlayer.NaturalCompletion is not null);
                            Assert(LoadedModeTrack(0) && !modePlayer.IsPlaying,
                                "Natural completion is deferred during a modal dialog");
                        }
                        finally { session.ModalDialogOpen = false; }
                        await ClickModeStop();
                        await Task.Delay(300);
                        Assert(LoadedModeTrack(0) && !modePlayer.IsPlaying && modePlayer.NaturalCompletion is null,
                            "Stop supersedes deferred natural completion");

                        session.ModalDialogOpen = true;
                        try
                        {
                            await modePlayer.PlayAsync();
                            await WaitModeAsync(() => modePlayer.NaturalCompletion is not null);
                        }
                        finally { session.ModalDialogOpen = false; }
                        await window.HandleShortcutAsync(Key.Left, ModifierKeys.Control);
                        await Task.Delay(300);
                        Assert(LoadedModeTrack(0) && !modePlayer.IsPlaying && modePlayer.NaturalCompletion is null
                            && modePlayer.GetCurrentTime() < modePlayer.GetDuration(),
                            "Backward seek supersedes deferred natural completion");

                        // In Repeat All, an unreadable next track must stop instead of cycling.
                        string brokenModePath = Path.Combine(modeFolder, "03.wav");
                        File.WriteAllText(brokenModePath, "not audio");
                        await window.Controller.LoadFolderAsync(modeFolder);
                        await StartModeTrack(1);
                        await WaitModeAsync(() => !modePlayer.IsLoaded && !window.Controller.Session.IsBusy);
                        Assert(!modePlayer.IsPlaying && modePlayer.NaturalCompletion is null,
                            "Unreadable automatic next file stops playback");

                        File.Delete(brokenModePath);
                        string emptyModePath = Path.Combine(modeFolder, "03.wav");
                        using (var emptyWriter = new WaveFileWriter(emptyModePath, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1))) { }
                        await window.Controller.LoadFolderAsync(modeFolder);
                        await StartModeTrack(1);
                        await WaitModeAsync(() => !modePlayer.IsLoaded && !window.Controller.Session.IsBusy);
                        Assert(!modePlayer.IsPlaying && modePlayer.NaturalCompletion is null,
                            "Zero-duration automatic next file stops playback");
                        File.Delete(emptyModePath);

                        modeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        string shortModePath = Path.Combine(modeFolder, "03.wav");
                        using (var shortWriter = new WaveFileWriter(shortModePath, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
                            shortWriter.WriteSamples(new float[44], 0, 44);
                        await window.Controller.LoadFolderAsync(modeFolder);
                        for (int attempt = 0; attempt < 4; attempt++)
                        {
                            await StartModeTrack(2);
                            await WaitModeAsync(() => !modePlayer.IsPlaying && modePlayer.GetCurrentTime() == modePlayer.GetDuration());
                            Assert(LoadedModeTrack(2), "Very short audio completes without stale Playing state");
                        }
                    }
                    finally
                    {
                        await window.Controller.LoadFolderAsync(audioFolder);
                        await window.Controller.Silence.WaitForPendingAsync();
                        Directory.Delete(modeFolder, true);
                    }
                    string recoveryPath = Path.Combine(audioFolder, "recovery.wav");
                    using (var writer = new WaveFileWriter(recoveryPath, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
                        writer.WriteSamples(new float[44100], 0, 44100);
                    var recoveryTrack = new AudioTrack { FilePath = recoveryPath };
                    await window.Controller.PlayTrackAsync(recoveryTrack);
                    if (window.Controller.Silence.AnalysisTask is Task recoveryAnalysis) await recoveryAnalysis;
                    await Field<PlayerEngine>(window.Controller.Session, "player")!.StopAsync();
                    string brokenPath = Path.Combine(audioFolder, "broken.wav");
                    File.WriteAllText(brokenPath, "not an audio file");
                    bool failedOpen = false;
                    try { await window.Controller.PlayTrackAsync(new AudioTrack { FilePath = brokenPath }); }
                    catch (Exception) { failedOpen = true; }
                    Assert(failedOpen, "Broken replacement track is rejected");
                    await window.Controller.PlayTrackAsync(recoveryTrack);
                    Assert(Field<PlayerEngine>(window.Controller.Session, "player")!.IsLoaded, "Original track can reopen after a failed replacement");
                    if (window.Controller.Silence.AnalysisTask is Task recoveredAnalysis) await recoveredAnalysis;
                    var scriptPlayer = Field<PlayerEngine>(window.Controller.Session, "player")!;
                    string seekPath = Path.Combine(audioFolder, "script-seek-test.wav");
                    using (var writer = new WaveFileWriter(seekPath, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
                        writer.WriteSamples(new float[44100 * 12], 0, 44100 * 12);
                    await window.Controller.PlayTrackAsync(new AudioTrack { FilePath = seekPath });
                    window.Controller.Session.BeginPreview(2);
                    window.Controller.Session.BeginPreview(4);
                    Assert(scriptPlayer.IsPaused && slider.Value == 4 &&
                        window.Controller.Session.PreviewDebounce == TimeSpan.FromMilliseconds(150),
                        "Scroll preview pauses audio, updates slider and retains latest target");
                    await window.Controller.Session.CommitPreviewAsync();
                    Assert(scriptPlayer.IsPlaying && scriptPlayer.GetCurrentTime().TotalSeconds >= 3.9,
                        "Committed scroll seek resumes playing audio at latest target");
                    scriptPlayer.Pause();
                    window.Controller.Session.BeginPreview(2);
                    await window.Controller.Session.CommitPreviewAsync();
                    Assert(scriptPlayer.IsPaused && Math.Abs(scriptPlayer.GetCurrentTime().TotalSeconds - 2) < 0.1,
                        "Committed scroll seek preserves explicit pause");
                    await scriptPlayer.PlayAsync();
                    window.Controller.Session.BeginPreview(6);
                    await window.Controller.Session.CancelPreviewAsync();
                    Assert(scriptPlayer.IsPlaying && scriptPlayer.GetCurrentTime().TotalSeconds < 5,
                        "Cancel pending scroll navigation restores playback without seeking");
                    window.Controller.Session.BeginPreview(6);
                    var settingsClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var closeSettingsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                    closeSettingsTimer.Tick += (_, _) =>
                    {
                        var activeDialog = app.Windows.OfType<SettingsWindow>().SingleOrDefault(w => w.IsVisible);
                        if (activeDialog is null) return;
                        closeSettingsTimer.Stop();
                        activeDialog.Close();
                        settingsClosed.TrySetResult();
                    };
                    closeSettingsTimer.Start();
                    try
                    {
                        settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await settingsClosed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    }
                    finally { closeSettingsTimer.Stop(); }
                    Assert(scriptPlayer.IsPlaying && window.Controller.Session.PreviewPosition is null,
                        "Opening and canceling Settings restores pending scroll playback intent");
                    window.Controller.Session.BeginPreview(6);
                    await window.HandleShortcutAsync(Key.Space);
                    Assert(scriptPlayer.IsPaused && window.Controller.Session.PreviewPosition is null,
                        "Space cancels scroll preview and honors intended pause");
                    window.Controller.Session.BeginPreview(6);
                    await window.HandleShortcutAsync(Key.Space);
                    Assert(scriptPlayer.IsPlaying, "Space from explicitly paused preview resumes playback");
                    // A consumed transaction cannot replay its old target.
                    window.Controller.Session.BeginPreview(5);
                    await window.Controller.Session.CommitPreviewAsync();
                    double committedPosition = scriptPlayer.GetCurrentTime().TotalSeconds;
                    await window.Controller.Session.CommitPreviewAsync();
                    Assert(scriptPlayer.IsPlaying && committedPosition >= 4.9 &&
                        scriptPlayer.GetCurrentTime().TotalSeconds >= committedPosition &&
                        window.Controller.Session.PreviewPosition is null,
                        "Completed script navigation is consumed exactly once");
                    window.Controller.Session.BeginPreview(6);
                    stopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (window.Controller.Session.ActiveOperation is Task stoppingScript) await stoppingScript;
                    Assert(!scriptPlayer.IsPlaying && scriptPlayer.GetCurrentTime() == TimeSpan.Zero &&
                        !window.Controller.Session.PreviewTimerEnabled,
                        "Stop cancels pending scroll seek without restarting");
                    if (window.Controller.Silence.AnalysisTask is Task seekAnalysis) await seekAnalysis;
                    await scriptPlayer.CloseAsync();
                    File.Delete(seekPath);
                    if (Directory.GetFiles(audioFolder, "*.mp3").Length == 0)
                    {
                        Console.WriteLine("SKIP audio-dependent UI checks: no local MP3 available.");
                        return;
                    }
                    Assert(list.Columns.Count == 4 && list.Items.Count > 0, "Four columns and a nonempty audio list");
                    Assert(list.Columns[2].Header?.ToString() == "时长", "Duration header is shortened");
                    list.UpdateLayout();
                    var row0 = (DataGridRow)list.ItemContainerGenerator.ContainerFromIndex(0);
                    var row1 = (DataGridRow)list.ItemContainerGenerator.ContainerFromIndex(1);
                    var rowCell = FindVisual<DataGridCell>(row0)!;
                    Assert(row0.FocusVisualStyle is null && rowCell.FocusVisualStyle is null, "DataGrid rows and cells have no focus visual");
                    Assert(ColorOf(row0.Background) == "#FFFFFFFF" && ColorOf(row1.Background) == "#FFF0F0F0", "Alternating white and light grey rows");
                    list.SelectedIndex = 0;
                    await window.HandleShortcutAsync(Key.Space);
                    Task? analysis = window.Controller.Silence.AnalysisTask;
                    if (analysis is not null) await analysis;
                    var player = Field<PlayerEngine>(window.Controller.Session, "player")!;
                    Assert(player.PlaybackState == PlaybackState.Playing, "Space opens selected track and plays");
                    playButton.Focus();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert(ColorOf(row0.Background) == "#FFE0DFC6", "Playing row retains active color outside row focus");
                    var markerCell = FindVisual<DataGridCell>(row0)!;
                    var playingMark = FindVisual<System.Windows.Shapes.Path>(markerCell)!;
                    Point markerPosition = playingMark.TransformToAncestor(markerCell).Transform(new Point(0, 0));
                    Assert(playingMark.Visibility == Visibility.Visible && Math.Abs(markerPosition.X - (markerCell.ActualWidth - playingMark.ActualWidth) / 2) < 1, "Playing triangle is horizontally centered");
                    await window.HandleShortcutAsync(Key.Space);
                    Assert(player.PlaybackState == PlaybackState.Paused, "Space pauses");
                    Assert(((AudioTrack)row0.Item).IsPaused && ColorOf(row0.Background) == "#FFE0DFC6", "Paused row retains bars and active color");
                    Assert(playingMark.Visibility == Visibility.Collapsed && FindVisual<StackPanel>(markerCell)!.Visibility == Visibility.Visible, "Pause replaces triangle with two bars");
                    var focusCell = FindVisual<DataGridCell>(row1)!;
                    focusCell.Focus();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert(row1.IsKeyboardFocusWithin && ColorOf(row1.Background) == "#FFF2C867", "Focused row uses gold background");
                    Assert(ColorOf(row0.Background) == "#FFE0DFC6", "Paused row stays active when another row focused");
                    playButton.Focus();
                    double before = player.GetCurrentTime().TotalSeconds;
                    await window.HandleShortcutAsync(Key.Right);
                    Assert(player.PlaybackState == PlaybackState.Paused && player.GetCurrentTime().TotalSeconds > before, "Paused navigation remains paused");
                    await player.SeekAsync(TimeSpan.FromSeconds(1));
                     await window.HandleShortcutAsync(Key.Left, ModifierKeys.Control);
                     Assert(player.GetCurrentTime() == TimeSpan.Zero && player.IsPaused, "Timed backward seek clamps to zero and stays paused");
                     await window.HandleShortcutAsync(Key.Right, ModifierKeys.Control);
                     Assert(Math.Abs(player.GetCurrentTime().TotalSeconds - Math.Min(4, player.GetDuration().TotalSeconds)) < 0.1 && player.IsPaused, "Timed forward seek uses default step and stays paused");
                     var ranges = SilenceResultStore.TryLoad(((AudioTrack)list.Items[0]).FilePath, new SilenceOptions())!;
                    await player.SeekAsync(ranges[0].Middle);
                    await window.HandleShortcutAsync(Key.Space);
                    double first = player.GetCurrentTime().TotalSeconds;
                    Task a = window.HandleShortcutAsync(Key.Right);
                    Task b = window.HandleShortcutAsync(Key.Right);
                    await Task.WhenAll(a, b);
                    Assert(player.GetCurrentTime() >= ranges[2].Middle, "Two rapid presses advance two pauses");
                    await window.HandleShortcutAsync(Key.Space);
                    list.SelectedIndex = 1;
                    var loaded = window.Controller.Session.LoadedTrack!;
                    Assert(!ReferenceEquals(list.SelectedItem, loaded), "Selection independent of loaded track");

                    playButton.Focus();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    var sourceOutside = PresentationSource.FromVisual(window)!;
                    var outsideEnter = new KeyEventArgs(Keyboard.PrimaryDevice, sourceOutside, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    playButton.RaiseEvent(outsideEnter);
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    Assert(ReferenceEquals(window.Controller.Session.LoadedTrack, loaded), "Enter outside the focused table row does not play the selected row");

                    var row1CellForEnter = FindVisual<DataGridCell>(row1)!;
                    row1CellForEnter.Focus();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    var rowEnter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    row1CellForEnter.RaiseEvent(rowEnter);
                    Assert(rowEnter.Handled, "Focused table row consumes Enter");
                    Task? enterOperation = window.Controller.Session.ActiveOperation;
                    if (enterOperation is not null) await enterOperation;
                    Task? enterAnalysis = window.Controller.Silence.AnalysisTask;
                    if (enterAnalysis is not null) await enterAnalysis;
                    var row1Track = (AudioTrack)row1.Item;
                    Assert(ReferenceEquals(window.Controller.Session.LoadedTrack, row1Track) && player.PlaybackState == PlaybackState.Playing, "Enter on focused table row plays that row");

                    await player.SeekAsync(TimeSpan.FromSeconds(2));
                    row1CellForEnter.Focus();
                    var currentRowEnter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    row1CellForEnter.RaiseEvent(currentRowEnter);
                    Task? currentEnterOperation = window.Controller.Session.ActiveOperation;
                    if (currentEnterOperation is not null) await currentEnterOperation;
                    Assert(player.PlaybackState == PlaybackState.Playing && player.GetCurrentTime() > TimeSpan.FromSeconds(1.8), "Enter on the already playing row does not restart it");

                    foreach (string name in new[] { "OpenButton", "PlayButton", "StopButton", "TrackList", "ProgressSlider" })
                    {
                        var control = (UIElement)window.FindName(name);
                        control.Focus();
                        foreach (Key key in new[] { Key.Space, Key.Left, Key.Right })
                        {
                            var source = PresentationSource.FromVisual(window)!;
                            var down = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                            control.RaiseEvent(down);
                            Assert(down.Handled, name + " consumes " + key + " down");
                            Task? operation = window.Controller.Session.ActiveOperation;
                            if (operation is not null) await operation;
                            Task? navigation = window.Controller.NavigationTask;
                            if (navigation is not null) await navigation;
                            await Dispatcher.Yield(DispatcherPriority.Background);
                            var up = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyUpEvent };
                            control.RaiseEvent(up);
                            Assert(up.Handled, name + " consumes " + key + " up");
                        }
                    }
                    await player.SeekAsync(player.GetDuration() - TimeSpan.FromMilliseconds(50));
                    await player.PlayAsync();
                    var output = Field<WaveOutEvent>(player, "output")!;
                    var wait = System.Diagnostics.Stopwatch.StartNew();
                    // Delay UI dispatch to expose a pending natural-end callback.
                    while (output.PlaybackState != PlaybackState.Stopped && wait.Elapsed < TimeSpan.FromSeconds(3)) Thread.Sleep(5);
                    Assert(output.PlaybackState == PlaybackState.Stopped, "Audio reaches natural end");
                    await player.PlayAsync();
                    await Task.Delay(100);
                    Assert(player.PlaybackState == PlaybackState.Playing && player.GetCurrentTime() < TimeSpan.FromSeconds(2), "Restart survives pending natural-end callback");
                    player.Pause();
                    var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent };
                    slider.RaiseEvent(click);
                    Assert(window.Controller.Session.IsDragging, "Track click starts seek interaction even when handled");
                    slider.Value = 5;
                    var release = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseUpEvent };
                    window.RaiseEvent(release);
                    Task? seek = window.Controller.Session.ActiveOperation;
                    if (seek is not null) await seek;
                    Assert(Math.Abs(player.GetCurrentTime().TotalSeconds - 5) < 0.15 && !window.Controller.Session.IsDragging, "Track click released outside slider commits seek");
                    await player.StopAsync();
                    Assert(player.GetCurrentTime() == TimeSpan.Zero, "Stop resets position");
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    playButton.Focus();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert(!((AudioTrack)row0.Item).IsActive && ColorOf(row0.Background) == "#FFFFFFFF", "Stopped unfocused row restores original background");
                    window.UpdateLayout();
                    string folder = Path.GetFullPath("docs/test-results");
                    Directory.CreateDirectory(folder);
                    var content = (FrameworkElement)window.Content;
                    int width = (int)(content.ActualWidth + content.Margin.Left + content.Margin.Right);
                    int height = (int)(content.ActualHeight + content.Margin.Top + content.Margin.Bottom);
                    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    var visual = new DrawingVisual();
                    using (var drawing = visual.RenderOpen())
                    {
                        drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
                        drawing.DrawRectangle(new VisualBrush(content), null, new Rect(content.Margin.Left, content.Margin.Top, content.ActualWidth, content.ActualHeight));
                    }
                    bitmap.Render(visual);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(Path.Combine(folder, "window-preview.png"))) png.Save(stream);
                    window.Close();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert(!window.IsVisible && !player.IsLoaded, "Window closes and releases audio");
                    foreach (string path in Directory.GetFiles(audioFolder, "*.mp3"))
                    {
                        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                        Assert(file.Length > 0, "File released: " + Path.GetFileName(path));
                    }
                    window = new MainWindow(settingsStore) { ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000 };
                    window.Show();
                    if (window.Controller.Session.ActiveOperation is Task restoredStartup) await restoredStartup;
                    await window.Controller.LoadFolderAsync(audioFolder);
                    await window.HandleShortcutAsync(Key.Space);
                    Assert(window.Controller.Silence.IsAnalyzing, "Analysis in progress before close");
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (_, _) => closed.TrySetResult();
                    window.Close();
                    await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert(!Field<PlayerEngine>(window.Controller.Session, "player")!.IsLoaded, "Close during analysis releases player");
                    using (var file = new FileStream(Directory.GetFiles(audioFolder, "*.mp3")[0], FileMode.Open, FileAccess.Read, FileShare.None))
                        Assert(file.Length > 0, "Close during analysis releases analysis reader");
                }
                catch (Exception ex) { Console.WriteLine("FAIL UI: " + ex); failed++; }
                finally
                {
                    if (window is not null && window.IsVisible) { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
                    Console.WriteLine($"UI tests: {passed} passed, {failed} failed");
                    done.TrySetResult(failed);
                    app.Shutdown();
                }
            };
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static T? Field<T>(object instance, string name) where T : class
        => (T?)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);
    private static T FieldValue<T>(object instance, string name)
        => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static string ColorOf(Brush brush) => ((SolidColorBrush)brush).Color.ToString();

    private static T? FindVisual<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            T? found = FindVisual<T>(child);
            if (found is not null) return found;
        }
        return null;
    }
}

