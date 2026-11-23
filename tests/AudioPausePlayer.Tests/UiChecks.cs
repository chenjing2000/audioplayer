using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioPausePlayer;
using NAudio.Wave;

internal static class UiChecks
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    public static Task<int> RunAsync(string audioFolder)
    {
        var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var appXml = System.Xml.Linq.XDocument.Load(Path.GetFullPath("EnglishAudioPlayer/App.xaml"));
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
                    var settingsStore = new SettingsStore(Path.Combine(audioFolder, "UiPlayer.settings.json"));
                    window = new MainWindow(settingsStore);
                    Assert(File.Exists(settingsStore.FilePath), "First launch initializes settings file");
                    Rect workArea = SystemParameters.WorkArea;
                    double expectedLeft = Math.Round(workArea.Left + (workArea.Width - window.Width) / 2);
                    double expectedTop = Math.Round(workArea.Top + (workArea.Height - window.Height) / 2);
                    Assert(Math.Abs(window.Left - expectedLeft) < 0.01 && Math.Abs(window.Top - expectedTop) < 0.01, "Window starts centered with rounded coordinates");
                    window.ShowInTaskbar = false; window.ShowActivated = false; window.Left = -20000; window.Top = -20000;
                    window.Show();
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
                    toasts.Show("测试信息", ToastLevel.Info);
                    Assert(toastStack.Children.Count == 1 &&
                        FindVisual<TextBlock>((Border)toastStack.Children[0])!.Text == "普通：测试信息",
                        "Normal toasts use the information prefix");
                    toasts.Show("测试警告", ToastLevel.Warning);
                    toasts.Show("测试错误", ToastLevel.Error);
                    Assert(toastStack.Children.Count == 3 &&
                        FindVisual<TextBlock>((Border)toastStack.Children[0])!.Text == "错误：测试错误",
                        "Newest visible toast is on top");
                    toasts.Show("排队消息", ToastLevel.Info);
                    Assert(toastStack.Children.Count == 3, "Only three toast slots are shown");
                    FindVisual<Button>((Border)toastStack.Children[2])!
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(350);
                    Assert(toastStack.Children.Count == 3 &&
                        FindVisual<TextBlock>((Border)toastStack.Children[0])!.Text == "普通：排队消息",
                        "Closing the oldest toast dequeues the next message");
                    await Task.Delay(5500);
                    Assert(toastStack.Children.OfType<Border>().Any(x =>
                        FindVisual<TextBlock>(x)?.Text == "错误：测试错误"), "Error toast persists");
                    toasts.Clear();
                    var active = toasts.ShowTask("持续检测");
                    await Task.Delay(5300);
                    Assert(toastStack.Children.Count == 1, "Task toast remains visible");
                    toasts.CompleteTask(active, "检测完成", ToastLevel.Info, TimeSpan.FromMilliseconds(150));
                    await Task.Delay(350);
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
                        string original = Path.Combine(refreshFolder, "original.wav");
                        string renamed = Path.Combine(refreshFolder, "renamed.wav");
                        string added = Path.Combine(refreshFolder, "added.wav");
                        using (var writer = new WaveFileWriter(original, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
                            writer.WriteSamples(new float[44100], 0, 44100);
                        await window.LoadFolderAsync(refreshFolder);
                        Assert(list.Items.Count == 1 && refreshButton.IsEnabled, "Refresh fixture loads one track");
                        list.SelectedIndex = 0;
                        File.Move(original, renamed);
                        using (var writer = new WaveFileWriter(added, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
                            writer.WriteSamples(new float[44100], 0, 44100);
                        refreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (Field<Task>(window, "activeOperation") is Task refreshAction) await refreshAction;
                        Assert(list.Items.Count == 2 &&
                            list.Items.OfType<AudioTrack>().Select(track => Path.GetFileName(track.FilePath))
                                .SequenceEqual(new[] { "added.wav", "renamed.wav" }) &&
                            list.SelectedItem is null && !Field<AudioPlayer>(window, "player")!.IsLoaded,
                            "Refresh reloads added and renamed files, clears selection and closes playback");
                        File.Delete(added);
                        refreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (Field<Task>(window, "activeOperation") is Task removedAction) await removedAction;
                        Assert(list.Items.Count == 1 &&
                            Path.GetFileName(((AudioTrack)list.Items[0]).FilePath) == "renamed.wav",
                            "Refresh drops deleted audio files");

                        Directory.Delete(refreshFolder, true);
                        refreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (Field<Task>(window, "activeOperation") is Task failedAction) await failedAction;
                        Assert(list.Items.Count == 1 && refreshButton.IsEnabled,
                            "Failed refresh retains the old list and folder selection");
                        toasts.Clear();
                        await window.LoadFolderAsync(audioFolder);
                    }
                    finally
                    {
                        if (Directory.Exists(refreshFolder)) Directory.Delete(refreshFolder, true);
                    }


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

                    settingsButton.ApplyTemplate();
                    var settingsBorder = (Border)settingsButton.Template.FindName("ButtonBorder", settingsButton);
                    var settingsImage = FindVisual<Image>(settingsButton)!;
                    Assert(Math.Abs(settingsButton.Width - 36) < 0.01 && Math.Abs(settingsButton.Height - 36) < 0.01 &&
                           Math.Abs(settingsBorder.CornerRadius.TopLeft - 18) < 0.01, "Settings button is circular");
                    Assert(Math.Abs(settingsImage.Width - 28) < 0.01 && Math.Abs(settingsImage.Height - 28) < 0.01, "Settings icon is 28x28");
                    Assert(settingsBorder.BorderThickness == new Thickness(0) && ColorOf(settingsBorder.Background) == "#00FFFFFF",
                        "Settings button has no border or visible background");
                    Assert(Grid.GetColumn(stopButton) == 4 && Grid.GetColumn(trimButton) == 5 &&
                        Grid.GetColumn(refreshButton) == 6 && Grid.GetColumn(settingsButton) == 7,
                        "Refresh button is placed between trim and settings");
                    Assert(new[] { playButton, stopButton, trimButton, refreshButton }
                        .All(button => Math.Abs(button.Margin.Right - 5) < 0.01),
                        "Five right toolbar buttons use 5-unit gaps");
                    Assert(Math.Abs(slider.Height - 36) < 0.01, "Progress slider has the same control height as the buttons");
                    window.UpdateLayout();
                    var sliderTrack = FindVisual<System.Windows.Controls.Primitives.Track>(slider);
                    double buttonCenterY = playButton.TranslatePoint(new Point(0, playButton.ActualHeight / 2), window).Y;
                    double sliderCenterY = slider.TranslatePoint(new Point(0, slider.ActualHeight / 2), window).Y;
                    Assert(Math.Abs(sliderCenterY - buttonCenterY) < 1.5,
                        "Progress control is vertically centered with the buttons");
                    Assert(sliderTrack is not null, "Progress slider exposes a visible WPF track");
                    Assert(settingsButton.ToolTip?.ToString() == "设置", "Settings gear has the updated name");
                    Assert(settingsButton.HorizontalAlignment == HorizontalAlignment.Center && settingsButton.VerticalAlignment == VerticalAlignment.Center, "Settings button is centered in the top row");
                    Assert(settingsButton.FocusVisualStyle is null, "Settings button has no focus visual");
                    foreach (string key in new[] { "OpenFolderIcon", "PlayIcon", "PauseIcon", "StopIcon", "TrimIcon", "RefreshIcon", "SettingsIcon" })
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
                    Assert(settingsWindow.Title == "设置", "Settings dialog title has the updated name");
                    Assert(Math.Abs(settingsWindow.Width - 324) < 0.01, "Settings window width is 324");
                    Assert(Math.Abs(settingsWindow.Height - 390) < 0.01, "Settings window height is 390");
                    foreach (string textBoxName in new[] { "FrameMillisecondsTextBox", "MinimumSilenceTextBox", "SilenceThresholdTextBox", "NavigationToleranceTextBox", "AllowedSilenceTextBox" })
                    {
                        var textBox = (TextBox)settingsWindow.FindName(textBoxName);
                        Assert(Math.Abs(textBox.Width - 80) < 0.01 && textBox.TextAlignment == TextAlignment.Center && textBox.VerticalContentAlignment == VerticalAlignment.Center && !textBox.AllowDrop, textBoxName + " retains input size and alignment");
                        textBox.ApplyTemplate();
                        Assert(FindVisual<Border>(textBox)!.CornerRadius.TopLeft == 6, "Input has rounded corners");
                    }
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
                    foreach (Button button in new[] { openButton, playButton, stopButton, trimButton })
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
                    if (WaveOut.DeviceCount == 0)
                    {
                        Console.WriteLine("SKIP UI audio-device playback checks: no waveOut device is available.");
                        return;
                    }
                    string recoveryPath = Path.Combine(audioFolder, "recovery.wav");
                    using (var writer = new WaveFileWriter(recoveryPath, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
                        writer.WriteSamples(new float[44100], 0, 44100);
                    var recoveryTrack = new AudioTrack { FilePath = recoveryPath, Title = "Recovery" };
                    MethodInfo playTrack = typeof(MainWindow).GetMethod("PlayTrackAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    await (Task)playTrack.Invoke(window, new object[] { recoveryTrack })!;
                    if (Field<Task>(window, "analysisTask") is Task recoveryAnalysis) await recoveryAnalysis;
                    await Field<AudioPlayer>(window, "player")!.StopAsync();
                    string brokenPath = Path.Combine(audioFolder, "broken.wav");
                    File.WriteAllText(brokenPath, "not an audio file");
                    bool failedOpen = false;
                    try { await (Task)playTrack.Invoke(window, new object[] { new AudioTrack { FilePath = brokenPath } })!; }
                    catch (Exception) { failedOpen = true; }
                    Assert(failedOpen, "Broken replacement track is rejected");
                    await (Task)playTrack.Invoke(window, new object[] { recoveryTrack })!;
                    Assert(Field<AudioPlayer>(window, "player")!.IsLoaded, "Original track can reopen after a failed replacement");
                    if (Field<Task>(window, "analysisTask") is Task recoveredAnalysis) await recoveredAnalysis;
                    await Field<AudioPlayer>(window, "player")!.CloseAsync();
                    if (Directory.GetFiles(audioFolder, "*.mp3").Length == 0)
                    {
                        Console.WriteLine("SKIP audio-dependent UI checks: no local MP3 available.");
                        return;
                    }
                    Assert(list.Columns.Count == 4 && list.Items.Count == 3, "Four columns and three MP3 files");
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
                    var player = Field<AudioPlayer>(window, "player")!;
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
                    await window.LoadFolderAsync(audioFolder);
                    await window.HandleShortcutAsync(Key.Space);
                    Assert(FieldValue<bool>(window, "analyzing"), "Analysis in progress before close");
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (_, _) => closed.TrySetResult();
                    window.Close();
                    await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert(!Field<AudioPlayer>(window, "player")!.IsLoaded, "Close during analysis releases player");
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

