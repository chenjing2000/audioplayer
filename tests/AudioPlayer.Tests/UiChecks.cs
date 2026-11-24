using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioPlayer;
using PlayerEngine = global::AudioPlayer.PlaybackEngine;
using NAudio.Wave;

internal static class UiChecks
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    public static Task<int> RunAsync(string audioFolder)
    {
        var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
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
                    window = new MainWindow(settingsStore);
                    Assert(File.Exists(settingsStore.FilePath), "First launch initializes settings file");
                    Rect workArea = SystemParameters.WorkArea;
                    double expectedLeft = Math.Round(workArea.Left + (workArea.Width - window.Width) / 2);
                    double expectedTop = Math.Round(workArea.Top + (workArea.Height - window.Height) / 2);
                    Assert(Math.Abs(window.Left - expectedLeft) < 0.01 && Math.Abs(window.Top - expectedTop) < 0.01, "Window starts centered with rounded coordinates");
                    window.ShowInTaskbar = false; window.ShowActivated = false; window.Left = -20000; window.Top = -20000;
                    window.Show();
                    if (Field<Task>(window, "activeOperation") is Task startupAction) await startupAction;
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
                    await window.LoadFolderAsync(audioFolder);
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
                        await window.LoadFolderAsync(refreshFolder);
                        Assert(list.Items.Count == 1 && refreshButton.IsEnabled, "Recursive folder load includes nested audio");
                        var playlistStore = Field<PlaylistStore>(window, "playlistStore")!;
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
                        if (Field<Task>(window, "activeOperation") is Task refreshAction) await refreshAction;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        Assert(list.Items.Count == 2 &&
                            list.Items.OfType<AudioTrack>().Select(track => Path.GetFileName(track.FilePath))
                                .SequenceEqual(new[] { "added.wav", "renamed.wav" }) &&
                            list.SelectedItem is null && !Field<PlayerEngine>(window, "player")!.IsLoaded,
                            "Refresh reloads added and renamed files, clears selection and closes playback");
                        Assert(refreshButton.IsEnabled, "Refresh button re-enables after folder load");
                        File.Delete(added);
                        refreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (Field<Task>(window, "activeOperation") is Task removedAction) await removedAction;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        Assert(list.Items.Count == 1 &&
                            Path.GetFileName(((AudioTrack)list.Items[0]).FilePath) == "renamed.wav",
                            "Refresh drops deleted audio files");

                        string trimmed = Path.Combine(nested, "renamed.new.wav");
                        File.Copy(renamed, trimmed);
                        window.AddTrimmedOutput(trimmed);
                        window.AddTrimmedOutput(trimmed);
                        Assert(list.Items.Count == 2 && playlistStore.Load()!.Files.Contains(trimmed),
                            "Nested trim output inserts and persists exactly once");
                        string undiscovered = Path.Combine(nested, "new-file.wav");
                        File.Copy(renamed, undiscovered);
                        File.Delete(trimmed);
                        var reopened = new MainWindow(settingsStore, playlistStore)
                            { ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000 };
                        reopened.Show();
                        if (Field<Task>(reopened, "activeOperation") is Task restoreAction) await restoreAction;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        try
                        {
                            var restoredList = (DataGrid)reopened.FindName("TrackList");
                            Assert(restoredList.Items.Count == 1 &&
                                ((AudioTrack)restoredList.Items[0]).FilePath == renamed &&
                                Field<string>(reopened, "currentFolder") == refreshFolder &&
                                !Field<PlayerEngine>(reopened, "player")!.IsLoaded,
                                "Startup restores saved existing files without rescanning or playing");
                            Assert(playlistStore.Load()!.Files.SequenceEqual(new[] { renamed }),
                                "Startup prunes deleted saved paths");
                        }
                        finally { reopened.Close(); }
                        Directory.Delete(refreshFolder, true);
                        refreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (Field<Task>(window, "activeOperation") is Task failedAction) await failedAction;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        Assert(list.Items.Count == 0 && list.SelectedItem is null &&
                            Field<string>(window, "currentFolder") is null &&
                            !Field<PlayerEngine>(window, "player")!.IsLoaded &&
                            !refreshButton.IsEnabled && !playButton.IsEnabled && !stopButton.IsEnabled,
                            "Missing refresh folder clears playlist, folder path and playback controls");
                        Assert(playlistStore.Load() is { RootFolder: null, Files.Length: 0 },
                            "Failed scan persists the empty state");

                        // A failed scan from the Open Folder path clears the old state too.
                        await window.LoadFolderAsync(audioFolder);
                        Assert(refreshButton.IsEnabled &&
                            Field<string>(window, "currentFolder") == Path.GetFullPath(audioFolder),
                            "Valid folder can reopen after failed refresh");
                        bool failedScan = false;
                        try { await window.LoadFolderAsync(refreshFolder); }
                        catch (DirectoryNotFoundException) { failedScan = true; }
                        Assert(failedScan && list.Items.Count == 0 &&
                            Field<string>(window, "currentFolder") is null &&
                            !refreshButton.IsEnabled && !playButton.IsEnabled,
                            "Failed folder load clears previously loaded folder");
                        // A successfully scanned empty folder is not a scan failure.
                        string emptyFolder = Path.Combine(audioFolder, "ui-empty-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(emptyFolder);
                        try
                        {
                            await window.LoadFolderAsync(emptyFolder);
                            Assert(list.Items.Count == 0 &&
                                Field<string>(window, "currentFolder") == Path.GetFullPath(emptyFolder) &&
                                refreshButton.IsEnabled,
                                "Empty valid folder retains folder path and Refresh");
                        }
                        finally { Directory.Delete(emptyFolder); }
                        toasts.Clear();
                        await window.LoadFolderAsync(audioFolder);
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
                        await window.LoadFolderAsync(scrollFolder);
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
                        await window.LoadFolderAsync(audioFolder);
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
                        await window.LoadFolderAsync(resultFolder);
                        list.SelectedIndex = 0;
                        if (Field<Task>(window, "selectionTask") is Task missingCheck) await missingCheck;
                        Assert(!trimButton.IsEnabled && !File.Exists(SilenceResultStore.GetPath(source)),
                            "Selecting uncached audio does not trigger detection or enable trimming");

                        Assert(SilenceResultStore.LoadOrDetect(source, new SilenceSettings()).Saved,
                            "Synthetic selection fixture can persist results");
                        // M4A decoders can report one PCM sample more than TotalTime at EOF.
                        string resultPath = SilenceResultStore.GetPath(source);
                        var resultNode = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(resultPath))!;
                        resultNode["ranges"]![0]!["endTicks"] = resultNode["durationTicks"]!.GetValue<long>() + 208;
                        File.WriteAllText(resultPath, resultNode.ToJsonString());
                        list.SelectedIndex = -1;
                        list.SelectedIndex = 0;
                        if (Field<Task>(window, "selectionTask") is Task validCheck) await validCheck;
                        Assert(trimButton.IsEnabled,
                            "Cached silence enables scissors even with a one-sample EOF rounding difference");

                        File.SetLastWriteTimeUtc(source, File.GetLastWriteTimeUtc(source).AddSeconds(5));
                        list.SelectedIndex = -1;
                        list.SelectedIndex = 0;
                        if (Field<Task>(window, "selectionTask") is Task outdatedCheck) await outdatedCheck;
                        Assert(!trimButton.IsEnabled, "Changed source invalidates selected trim availability");
                        for (int i = 0; i < 12; i++)
                        {
                            list.SelectedIndex = -1;
                            list.SelectedIndex = 0;
                        }
                        await Task.WhenAll(Field<List<Task>>(window, "runningSelectionChecks")!);
                        Assert(!trimButton.IsEnabled, "Rapid selection changes leave stale caches disabled");
                        await window.LoadFolderAsync(audioFolder);
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
                    var modeDialog = new SilenceSettingsWindow(settingsStore.LoadOrCreate());
                    var readModeValues = typeof(SilenceSettingsWindow).GetMethod("TryReadValues", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    object?[] modeArgs = { null };
                    Assert((bool)readModeValues.Invoke(modeDialog, modeArgs)! && ((SilenceSettings)modeArgs[0]!).PlaybackMode == PlaybackMode.RepeatOne,
                        "Normal settings validation preserves playback mode");
                    ((Button)modeDialog.FindName("RestoreDefaultsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert((bool)readModeValues.Invoke(modeDialog, modeArgs)! && ((SilenceSettings)modeArgs[0]!).PlaybackMode == PlaybackMode.PlayOne
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

                    var settingsWindow = new SilenceSettingsWindow(new SilenceSettings());
                    Assert(((TabControl)settingsWindow.FindName("SettingsTabs")).Items.Count == 2, "Settings has audio and script tabs");
                    Assert((int)((ComboBox)settingsWindow.FindName("FontSizeBox")).SelectedItem == 18, "Script font defaults to 18 DIP");
                    Assert(settingsWindow.Title == "设置", "Settings dialog title has the updated name");
                    Assert(Math.Abs(settingsWindow.Width - 480) < 0.01, "Settings window width is 480");
                    Assert(Math.Abs(settingsWindow.Height - 420) < 0.01, "Settings window height is 420");
                    foreach (string textBoxName in new[] { "FrameMillisecondsTextBox", "MinimumSilenceTextBox", "SilenceThresholdTextBox", "NavigationToleranceTextBox", "AllowedSilenceTextBox", "BackwardStepTextBox", "ForwardStepTextBox" })
                    {
                        var textBox = (TextBox)settingsWindow.FindName(textBoxName);
                        Assert(Math.Abs(textBox.Width - 80) < 0.01 && textBox.TextAlignment == TextAlignment.Center && textBox.VerticalContentAlignment == VerticalAlignment.Center && !textBox.AllowDrop, textBoxName + " retains input size and alignment");
                        textBox.ApplyTemplate();
                        Assert(FindVisual<Border>(textBox)!.CornerRadius.TopLeft == 6, "Input has rounded corners");
                    }
                    var excludedBox = (TextBox)settingsWindow.FindName("ExcludedFolderNamesTextBox");
                     Assert(excludedBox.Text == "" && excludedBox.TextAlignment == TextAlignment.Left && Grid.GetRow(excludedBox) == 6, "Exclusion field defaults empty below explanatory text");
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
                     settingsWindow.Hide();
                     var backwardBox = (TextBox)settingsWindow.FindName("BackwardStepTextBox");
                     var forwardBox = (TextBox)settingsWindow.FindName("ForwardStepTextBox");
                     Assert(backwardBox.Text == "5" && forwardBox.Text == "4" && backwardBox.MaxLength == 2 && forwardBox.MaxLength == 2, "Step defaults and length");
                     MethodInfo stepInput = typeof(SilenceSettingsWindow).GetMethod("IsStepText", BindingFlags.Static | BindingFlags.NonPublic)!;
                     Assert(new[] { "", "0", "99", "05" }.All(v => (bool)stepInput.Invoke(null, new object[] { v })!)
                         && new[] { "+5", "-1", "100", "2.5", "５", "a" }.All(v => !(bool)stepInput.Invoke(null, new object[] { v })!), "Step input is ASCII digits only");
                     Assert(MainWindow.IsPlaybackShortcut(Key.Left, ModifierKeys.Control) && MainWindow.IsPlaybackShortcut(Key.Right, ModifierKeys.Control)
                         && MainWindow.IsPlaybackShortcut(Key.Space, ModifierKeys.None) && !MainWindow.IsPlaybackShortcut(Key.Space, ModifierKeys.Control)
                         && !MainWindow.IsPlaybackShortcut(Key.Left, ModifierKeys.Control | ModifierKeys.Shift), "Shortcut modifier routing");
                     MethodInfo positiveInput = typeof(SilenceSettingsWindow).GetMethod("IsPositiveIntegerText", BindingFlags.Static | BindingFlags.NonPublic)!;
                    MethodInfo signedInput = typeof(SilenceSettingsWindow).GetMethod("IsSignedIntegerText", BindingFlags.Static | BindingFlags.NonPublic)!;
                    Assert((bool)positiveInput.Invoke(null, new object[] { "+123" })! && !(bool)positiveInput.Invoke(null, new object[] { "-1" })! && !(bool)positiveInput.Invoke(null, new object[] { "1.5" })! && !(bool)positiveInput.Invoke(null, new object[] { "abc" })!, "Positive parameter boxes accept only digits and optional leading plus sign");
                    Assert((bool)signedInput.Invoke(null, new object[] { "-45" })! && (bool)signedInput.Invoke(null, new object[] { "+45" })! && !(bool)signedInput.Invoke(null, new object[] { "4-5" })! && !(bool)signedInput.Invoke(null, new object[] { "-4.5" })!, "Silence threshold box accepts digits and one leading sign only");
                    MethodInfo editableSeconds = typeof(SilenceSettingsWindow).GetMethod("IsEditableSilenceSeconds", BindingFlags.Static | BindingFlags.NonPublic)!;
                     MethodInfo completeSeconds = typeof(SilenceSettingsWindow).GetMethod("IsCompleteSilenceSeconds", BindingFlags.Static | BindingFlags.NonPublic)!;
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
                     MethodInfo readValues = typeof(SilenceSettingsWindow).GetMethod("TryReadValues",
                         BindingFlags.Instance | BindingFlags.NonPublic)!;
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
                    Field<List<Task>>(closingWindow, "runningSelectionChecks")!.Add(pendingSelection.Task);
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
                        var editWindow = app.Windows.OfType<SilenceSettingsWindow>().Single(w => w.IsVisible);
                        ((TextBox)editWindow.FindName("FrameMillisecondsTextBox")).Text = "25";
                        ((TextBox)editWindow.FindName("MinimumSilenceTextBox")).Text = "600";
                        ((Button)editWindow.FindName("OkButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }));
                    settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var saved = settingsStore.LoadOrCreate();
                    Assert(saved.FrameMilliseconds == 25 && saved.MinimumSilenceMilliseconds == 600
                        && Field<SilenceSettings>(window, "silenceSettings")!.FrameMilliseconds == 25,
                        "Settings dialog saves confirmed values before applying them");
                    var restoredWindow = new MainWindow(settingsStore);
                    Assert(Field<SilenceSettings>(restoredWindow, "silenceSettings")!.FrameMilliseconds == 25,
                        "Window restores saved settings on next launch");
                    restoredWindow.Close();
                    string invalidConfiguration = "{broken";
                    File.WriteAllText(settingsStore.FilePath, invalidConfiguration);
                    var invalidWindow = new MainWindow(settingsStore);
                    Assert(!FieldValue<bool>(invalidWindow, "settingsFileReady")
                        && FindVisual<StackPanel>((ToastHost)invalidWindow.FindName("ToastMessages"))!.Children.Count > 0
                        && File.ReadAllText(settingsStore.FilePath) == invalidConfiguration,
                        "Invalid configuration is preserved and reported inline");
                    invalidWindow.Close();
                    settingsStore.Save(saved);
                    window.BeginScriptSeek(double.NaN);
                    window.BeginScriptSeek(-1);
                    Assert(!Field<DispatcherTimer>(window, "scriptSeekTimer")!.IsEnabled &&
                        !FieldValue<double?>(window, "scriptSeekTarget").HasValue,
                        "Invalid script navigation targets do not arm the debounce timer");
                    await window.CommitScriptSeekAsync();
                    Assert(!Field<DispatcherTimer>(window, "scriptSeekTimer")!.IsEnabled,
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
                            var deadline = DateTime.UtcNow.AddSeconds(25);
                            while (!readerWindow.ReaderReady && readerWindow.ReaderFailure is null && DateTime.UtcNow < deadline)
                                await Task.Delay(100);
                            Assert(readerWindow.ReaderReady, "Embedded WebView2 reader completes ready handshake: " + readerWindow.ReaderFailure);
                            var reader = (Microsoft.Web.WebView2.Wpf.WebView2)readerWindow.FindName("Reader");
                            string renderedScriptText = "";
                            deadline = DateTime.UtcNow.AddSeconds(25);
                            while (DateTime.UtcNow < deadline)
                            {
                                renderedScriptText = await reader.CoreWebView2.ExecuteScriptAsync("document.querySelector('#text').textContent");
                                if (renderedScriptText.Contains("Hello, world.")) break;
                                await Task.Delay(100);
                            }
                            Assert(renderedScriptText.Contains("Hello, world."), "Embedded WebView2 reader renders discovered Script JSON");
                            string measured = await reader.CoreWebView2.ExecuteScriptAsync(
                                "measure(); update({position:1.5,playing:false,reset:false},true); geometry.length===2 && geometry[0].last===0.75 && geometry[1].last===0.25");
                            Assert(measured == "true", "Reader paints overlapping out-of-order intervals");
                            string rewind = await reader.CoreWebView2.ExecuteScriptAsync(
                                "update({position:2.5,playing:false,reset:false},true); update({position:0.5,playing:false,reset:false},true); geometry[0].last===0.25 && geometry[1].segments.every(s=>s.painted===0)");
                            Assert(rewind == "true", "Reader rewinds previously painted intervals");
                            string cleared = await reader.CoreWebView2.ExecuteScriptAsync(
                                "update({position:0,playing:false,reset:true},true); geometry.every(u=>u.segments.every(s=>s.painted===0))");
                            Assert(cleared == "true", "Reader reset clears painted intervals");

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
                                      followOffset(10,100,220)===120 &&
                                      followOffset(10,100,80)===0;
                                  } finally {followAnchors=saved;}
                                })()
                                """);
                            Assert(continuousFollow == "true", "Reader follows continuous time-position trajectory between visual lines");
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

                            string wheelSensitivity = await reader.CoreWebView2.ExecuteScriptAsync(
                                """
                                (() => {
                                  const originalScroll=window.scrollTo;
                                  const before=window.scrollY;
                                  let target;
                                  try {
                                    // Intercept the actual wheel handler's requested displacement,
                                    // avoiding document-boundary clamping and host seek side effects.
                                    window.scrollTo=(_,y)=>{target=y;};
                                    const lineHeight=parseFloat(getComputedStyle(document.documentElement).lineHeight);
                                    const cases=[[120,0,30],[-120,0,-30],[2,1,lineHeight/2],[-2,1,-lineHeight/2],[1,2,window.innerHeight/4]];
                                    return cases.every(([deltaY,deltaMode,expected])=>{
                                      target=undefined;
                                      const event=new WheelEvent('wheel',{deltaY,deltaMode,cancelable:true});
                                      window.dispatchEvent(event);
                                      return event.defaultPrevented && Math.abs(target-before-expected)<0.001;
                                    });
                                  } finally {window.scrollTo=originalScroll;}
                                })()
                                """);
                            Assert(wheelSensitivity == "true", "Reader wheel displacement is quartered for pixel, line and page deltas");

                            // Keep the synthetic article isolated from MainWindow's playback timer
                            // until the rendering assertions finish. No real audio is loaded here.
                            typeof(MainWindow).GetField("scriptWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
                                .SetValue(window, readerWindow);
                            var readerHandle = new System.Windows.Interop.WindowInteropHelper(readerWindow).Handle;
                            double readerWidth = readerWindow.Width, readerHeight = readerWindow.Height;
                            var originalCore = reader.CoreWebView2;
                            window.WindowState = WindowState.Minimized;
                            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                            Assert(readerWindow.Visibility != Visibility.Visible && !IsWindowVisible(readerHandle),
                                "Minimizing owner explicitly hides native reader window");
                            window.WindowState = WindowState.Normal;
                            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                            Assert(readerWindow.IsVisible && IsWindowVisible(readerHandle) &&
                                readerWindow.ReaderReady && ReferenceEquals(originalCore, reader.CoreWebView2) &&
                                readerWindow.Width == readerWidth && readerWindow.Height == readerHeight,
                                "Restoring owner shows same reader and preserves dimensions");
                            readerWindow.Hide();
                            window.WindowState = WindowState.Minimized;
                            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                            window.WindowState = WindowState.Normal;
                            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                            Assert(!readerWindow.IsVisible && !IsWindowVisible(readerHandle),
                                "Restoring owner leaves previously hidden reader hidden");
                        }
                        finally
                        {
                            typeof(MainWindow).GetField("scriptWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
                                .SetValue(window, null);
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
                        Console.WriteLine("SKIP UI script wheel playback checks (7 assertions plus Settings restoration): no waveOut device is available.");
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
                        await window.LoadFolderAsync(modeFolder);
                        var modePlayer = Field<PlayerEngine>(window, "player")!;
                        var modalField = typeof(MainWindow).GetField("modalDialogOpen", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        async Task WaitModeAsync(Func<bool> condition)
                        {
                            var deadline = DateTime.UtcNow.AddSeconds(8);
                            while ((!condition() || FieldValue<bool>(window, "operationBusy")) && DateTime.UtcNow < deadline) await Task.Delay(25);
                            Assert(condition() && !FieldValue<bool>(window, "operationBusy"), "Playback mode transition completes");
                        }
                        async Task ClickModeStop()
                        {
                            stopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            await WaitModeAsync(() => !FieldValue<bool>(window, "operationBusy") && !modePlayer.IsPlaying);
                        }
                        async Task StartModeTrack(int index)
                        {
                            // Use the normal play shortcut with the selected file on a fresh playlist.
                            if (modePlayer.IsLoaded && !ReferenceEquals(Field<AudioTrack>(window, "loadedTrack"), list.Items[index]))
                                await window.LoadFolderAsync(modeFolder);
                            list.SelectedIndex = index;
                            await window.HandleShortcutAsync(Key.Space);
                        }
                        bool LoadedModeTrack(int index) => ReferenceEquals(Field<AudioTrack>(window, "loadedTrack"), list.Items[index]);
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
                        modalField.SetValue(window, true);
                        try
                        {
                            await modePlayer.PlayAsync();
                            await WaitModeAsync(() => modePlayer.NaturalCompletion is not null);
                            Assert(LoadedModeTrack(0) && !modePlayer.IsPlaying,
                                "Natural completion is deferred during a modal dialog");
                        }
                        finally { modalField.SetValue(window, false); }
                        await ClickModeStop();
                        await Task.Delay(300);
                        Assert(LoadedModeTrack(0) && !modePlayer.IsPlaying && modePlayer.NaturalCompletion is null,
                            "Stop supersedes deferred natural completion");

                        modalField.SetValue(window, true);
                        try
                        {
                            await modePlayer.PlayAsync();
                            await WaitModeAsync(() => modePlayer.NaturalCompletion is not null);
                        }
                        finally { modalField.SetValue(window, false); }
                        await window.HandleShortcutAsync(Key.Left, ModifierKeys.Control);
                        await Task.Delay(300);
                        Assert(LoadedModeTrack(0) && !modePlayer.IsPlaying && modePlayer.NaturalCompletion is null
                            && modePlayer.GetCurrentTime() < modePlayer.GetDuration(),
                            "Backward seek supersedes deferred natural completion");

                        // In Repeat All, an unreadable next track must stop instead of cycling.
                        string brokenModePath = Path.Combine(modeFolder, "03.wav");
                        File.WriteAllText(brokenModePath, "not audio");
                        await window.LoadFolderAsync(modeFolder);
                        await StartModeTrack(1);
                        await WaitModeAsync(() => !modePlayer.IsLoaded && !FieldValue<bool>(window, "operationBusy"));
                        Assert(!modePlayer.IsPlaying && modePlayer.NaturalCompletion is null,
                            "Unreadable automatic next file stops playback");

                        File.Delete(brokenModePath);
                        string emptyModePath = Path.Combine(modeFolder, "03.wav");
                        using (var emptyWriter = new WaveFileWriter(emptyModePath, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1))) { }
                        await window.LoadFolderAsync(modeFolder);
                        await StartModeTrack(1);
                        await WaitModeAsync(() => !modePlayer.IsLoaded && !FieldValue<bool>(window, "operationBusy"));
                        Assert(!modePlayer.IsPlaying && modePlayer.NaturalCompletion is null,
                            "Zero-duration automatic next file stops playback");
                        File.Delete(emptyModePath);

                        modeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        string shortModePath = Path.Combine(modeFolder, "03.wav");
                        using (var shortWriter = new WaveFileWriter(shortModePath, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
                            shortWriter.WriteSamples(new float[44], 0, 44);
                        await window.LoadFolderAsync(modeFolder);
                        for (int attempt = 0; attempt < 4; attempt++)
                        {
                            await StartModeTrack(2);
                            await WaitModeAsync(() => !modePlayer.IsPlaying && modePlayer.GetCurrentTime() == modePlayer.GetDuration());
                            Assert(LoadedModeTrack(2), "Very short audio completes without stale Playing state");
                        }
                    }
                    finally
                    {
                        await window.LoadFolderAsync(audioFolder);
                        await Task.WhenAll(Field<List<Task>>(window, "runningAnalyses")!);
                        await Task.WhenAll(Field<List<Task>>(window, "runningSelectionChecks")!);
                        Directory.Delete(modeFolder, true);
                    }
                    string recoveryPath = Path.Combine(audioFolder, "recovery.wav");
                    using (var writer = new WaveFileWriter(recoveryPath, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
                        writer.WriteSamples(new float[44100], 0, 44100);
                    var recoveryTrack = new AudioTrack { FilePath = recoveryPath };
                    MethodInfo playTrack = typeof(MainWindow).GetMethod("PlayTrackAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    await (Task)playTrack.Invoke(window, new object[] { recoveryTrack })!;
                    if (Field<Task>(window, "analysisTask") is Task recoveryAnalysis) await recoveryAnalysis;
                    await Field<PlayerEngine>(window, "player")!.StopAsync();
                    string brokenPath = Path.Combine(audioFolder, "broken.wav");
                    File.WriteAllText(brokenPath, "not an audio file");
                    bool failedOpen = false;
                    try { await (Task)playTrack.Invoke(window, new object[] { new AudioTrack { FilePath = brokenPath } })!; }
                    catch (Exception) { failedOpen = true; }
                    Assert(failedOpen, "Broken replacement track is rejected");
                    await (Task)playTrack.Invoke(window, new object[] { recoveryTrack })!;
                    Assert(Field<PlayerEngine>(window, "player")!.IsLoaded, "Original track can reopen after a failed replacement");
                    if (Field<Task>(window, "analysisTask") is Task recoveredAnalysis) await recoveredAnalysis;
                    var scriptPlayer = Field<PlayerEngine>(window, "player")!;
                    string seekPath = Path.Combine(audioFolder, "script-seek-test.wav");
                    using (var writer = new WaveFileWriter(seekPath, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
                        writer.WriteSamples(new float[44100 * 12], 0, 44100 * 12);
                    await (Task)playTrack.Invoke(window, new object[] { new AudioTrack { FilePath = seekPath } })!;
                    window.BeginScriptSeek(2);
                    window.BeginScriptSeek(4);
                    Assert(scriptPlayer.IsPaused && slider.Value == 4 &&
                        Field<DispatcherTimer>(window, "scriptSeekTimer")!.Interval == TimeSpan.FromMilliseconds(150),
                        "Wheel preview pauses audio, updates slider and retains latest target");
                    await window.CommitScriptSeekAsync();
                    Assert(scriptPlayer.IsPlaying && scriptPlayer.GetCurrentTime().TotalSeconds >= 3.9,
                        "Committed wheel seek resumes playing audio at latest target");
                    scriptPlayer.Pause();
                    window.BeginScriptSeek(2);
                    await window.CommitScriptSeekAsync();
                    Assert(scriptPlayer.IsPaused && Math.Abs(scriptPlayer.GetCurrentTime().TotalSeconds - 2) < 0.1,
                        "Committed wheel seek preserves explicit pause");
                    await scriptPlayer.PlayAsync();
                    window.BeginScriptSeek(6);
                    await window.RestoreCanceledScriptSeekAsync();
                    Assert(scriptPlayer.IsPlaying && scriptPlayer.GetCurrentTime().TotalSeconds < 5,
                        "Cancel pending wheel navigation restores playback without seeking");
                    window.BeginScriptSeek(6);
                    var settingsClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var closeSettingsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                    closeSettingsTimer.Tick += (_, _) =>
                    {
                        var activeDialog = app.Windows.OfType<SilenceSettingsWindow>().SingleOrDefault(w => w.IsVisible);
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
                    Assert(scriptPlayer.IsPlaying && !FieldValue<double?>(window, "scriptSeekTarget").HasValue,
                        "Opening and canceling Settings restores pending wheel playback intent");
                    window.BeginScriptSeek(6);
                    await window.HandleShortcutAsync(Key.Space);
                    Assert(scriptPlayer.IsPaused && !FieldValue<double?>(window, "scriptSeekTarget").HasValue,
                        "Space cancels wheel preview and honors intended pause");
                    window.BeginScriptSeek(6);
                    await window.HandleShortcutAsync(Key.Space);
                    Assert(scriptPlayer.IsPlaying, "Space from explicitly paused preview resumes playback");
                    window.BeginScriptSeek(6);
                    stopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (Field<Task>(window, "activeOperation") is Task stoppingScript) await stoppingScript;
                    Assert(!scriptPlayer.IsPlaying && scriptPlayer.GetCurrentTime() == TimeSpan.Zero &&
                        !Field<DispatcherTimer>(window, "scriptSeekTimer")!.IsEnabled,
                        "Stop cancels pending wheel seek without restarting");
                    if (Field<Task>(window, "analysisTask") is Task seekAnalysis) await seekAnalysis;
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
                    Task? analysis = Field<Task>(window, "analysisTask");
                    if (analysis is not null) await analysis;
                    var player = Field<PlayerEngine>(window, "player")!;
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
                     var ranges = Field<List<SilenceRange>>(window, "ranges")!;
                    await player.SeekAsync(ranges[0].Middle);
                    await window.HandleShortcutAsync(Key.Space);
                    double first = player.GetCurrentTime().TotalSeconds;
                    Task a = window.HandleShortcutAsync(Key.Right);
                    Task b = window.HandleShortcutAsync(Key.Right);
                    await Task.WhenAll(a, b);
                    Assert(player.GetCurrentTime() >= ranges[2].Middle, "Two rapid presses advance two pauses");
                    await window.HandleShortcutAsync(Key.Space);
                    list.SelectedIndex = 1;
                    var loaded = Field<AudioTrack>(window, "loadedTrack")!;
                    Assert(!ReferenceEquals(list.SelectedItem, loaded), "Selection independent of loaded track");

                    playButton.Focus();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    var sourceOutside = PresentationSource.FromVisual(window)!;
                    var outsideEnter = new KeyEventArgs(Keyboard.PrimaryDevice, sourceOutside, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    playButton.RaiseEvent(outsideEnter);
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    Assert(ReferenceEquals(Field<AudioTrack>(window, "loadedTrack"), loaded), "Enter outside the focused table row does not play the selected row");

                    var row1CellForEnter = FindVisual<DataGridCell>(row1)!;
                    row1CellForEnter.Focus();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    var rowEnter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    row1CellForEnter.RaiseEvent(rowEnter);
                    Assert(rowEnter.Handled, "Focused table row consumes Enter");
                    Task? enterOperation = Field<Task>(window, "activeOperation");
                    if (enterOperation is not null) await enterOperation;
                    Task? enterAnalysis = Field<Task>(window, "analysisTask");
                    if (enterAnalysis is not null) await enterAnalysis;
                    var row1Track = (AudioTrack)row1.Item;
                    Assert(ReferenceEquals(Field<AudioTrack>(window, "loadedTrack"), row1Track) && player.PlaybackState == PlaybackState.Playing, "Enter on focused table row plays that row");

                    await player.SeekAsync(TimeSpan.FromSeconds(2));
                    row1CellForEnter.Focus();
                    var currentRowEnter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    row1CellForEnter.RaiseEvent(currentRowEnter);
                    Task? currentEnterOperation = Field<Task>(window, "activeOperation");
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
                            Task? operation = Field<Task>(window, "activeOperation");
                            if (operation is not null) await operation;
                            Task? navigation = Field<Task>(window, "navigationTask");
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
                    Assert(FieldValue<bool>(window, "dragging"), "Track click starts seek interaction even when handled");
                    slider.Value = 5;
                    var release = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseUpEvent };
                    window.RaiseEvent(release);
                    Task? seek = Field<Task>(window, "activeOperation");
                    if (seek is not null) await seek;
                    Assert(Math.Abs(player.GetCurrentTime().TotalSeconds - 5) < 0.15 && !FieldValue<bool>(window, "dragging"), "Track click released outside slider commits seek");
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
                    if (Field<Task>(window, "activeOperation") is Task restoredStartup) await restoredStartup;
                    await window.LoadFolderAsync(audioFolder);
                    await window.HandleShortcutAsync(Key.Space);
                    Assert(FieldValue<bool>(window, "analyzing"), "Analysis in progress before close");
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (_, _) => closed.TrySetResult();
                    window.Close();
                    await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert(!Field<PlayerEngine>(window, "player")!.IsLoaded, "Close during analysis releases player");
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

