using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace AudioPlayer;

public partial class ScriptWindow : Window
{
    private const string ReaderOrigin = "https://audioplayer.invalid";
    private const string ReaderDocument = ReaderOrigin + "/script.html";
    private readonly string? testCacheDirectory;
    private bool ownerClosing;
    private bool readerFailed;
    private ScriptAppearance appearance = new();
    private bool initialized;
    private bool ready;
    internal event Action<double>? NavigationPreview;
    internal event Action? NavigationCanceled;
    internal event Action<System.Windows.Input.Key, System.Windows.Input.ModifierKeys>? Shortcut;
    private int navigationEpoch;
    private (double Position, bool Playing, bool Reset)? playback;
    private (double Position, bool Playing, bool Reset)? sentPlayback;
    private string? audioPath;
    private CancellationTokenSource? scriptLoadCancellation;
    private ScriptArticle? article;
    private string message = "尚未加载原文";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ScriptWindow() : this(null) { }

    internal ScriptWindow(string? cacheDirectory)
    {
        testCacheDirectory = cacheDirectory;
        InitializeComponent();
        Loaded += InitializeReader;
    }

    internal bool ReaderReady => ready;
    internal string? ReaderFailure => readerFailed ? Status.Text : null;

    private async void InitializeReader(object sender, RoutedEventArgs e)
    {
        if (initialized) return;
        initialized = true;
        try
        {
            string folder = testCacheDirectory ?? Path.Combine(Path.GetDirectoryName(SettingsStore.GetExecutablePath())!, "cache", "webview2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: folder);
            if (ownerClosing) return;
            await Reader.EnsureCoreWebView2Async(environment);
            if (ownerClosing) return;
            var core = Reader.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.AddWebResourceRequestedFilter(ReaderOrigin + "/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) =>
            {
                Stream? stream = string.Equals(args.Request.Uri, ReaderDocument, StringComparison.OrdinalIgnoreCase)
                    ? typeof(ScriptWindow).Assembly.GetManifestResourceStream("AudioPlayer.web.script.html") : null;
                int code = stream is null ? 404 : 200;
                stream ??= new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Not found"));
                args.Response = environment.CreateWebResourceResponse(stream, code,
                    code == 200 ? "OK" : "Not Found",
                    "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff");
            };
            core.NavigationStarting += (_, args) =>
            {
                if (!string.Equals(args.Uri, ReaderDocument, StringComparison.OrdinalIgnoreCase))
                {
                    args.Cancel = true;
                    return;
                }
                ready = false;
                sentPlayback = null;
            };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.ProcessFailed += (_, args) => ShowFailure("WebView2 进程异常：" + args.ProcessFailedKind);
            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess && args.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                    ShowFailure("原文页面加载失败：" + args.WebErrorStatus);
            };
            core.WebMessageReceived += (_, args) =>
            {
                if (ownerClosing || readerFailed || !string.Equals(args.Source, ReaderDocument, StringComparison.OrdinalIgnoreCase)) return;
                if (args.WebMessageAsJson == "\"ready\"")
                {
                    if (ready) return;
                    ready = true;
                    Status.Visibility = Visibility.Collapsed;
                    Reader.Visibility = Visibility.Visible;
                    Post(new { type = "appearance", appearance });
                    SendArticle();
                    return;
                }
                ReceiveNavigation(args.WebMessageAsJson);
            };
            // Match ReadArticles: serve the embedded document at a fixed local HTTPS origin.
            // No extracted files or remote network resources are required.
            using (var resource = typeof(ScriptWindow).Assembly.GetManifestResourceStream("AudioPlayer.web.script.html"))
                if (resource is null) throw new InvalidDataException("缺少原文阅读资源。");
            core.Navigate(ReaderDocument);
            await Task.Delay(TimeSpan.FromSeconds(15));
            if (!ownerClosing && !ready && !readerFailed)
                ShowFailure("原文阅读器初始化超时：未收到页面 ready 消息。");
        }
        catch (Exception ex)
        {
            if (!ownerClosing) ShowFailure("WebView2 无法初始化：" + ex.Message);
        }
    }

    private void ReceiveNavigation(string raw)
    {
        if (!ready || !IsVisible || raw.Length > 2048) return;
        try
        {
            using var json = JsonDocument.Parse(raw);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("epoch", out var epoch) || epoch.ValueKind != JsonValueKind.Number || !epoch.TryGetInt32(out int id) || id != navigationEpoch ||
                !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return;
            if (type.GetString() == "shortcut")
            {
                if (!root.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String) return;
                var k = key.GetString() switch
                {
                    " " => System.Windows.Input.Key.Space,
                    "ArrowLeft" => System.Windows.Input.Key.Left,
                    "ArrowRight" => System.Windows.Input.Key.Right,
                    _ => System.Windows.Input.Key.None
                };
                var modifiers = root.TryGetProperty("control", out var control) && control.ValueKind == JsonValueKind.True
                    ? System.Windows.Input.ModifierKeys.Control : System.Windows.Input.ModifierKeys.None;
                Shortcut?.Invoke(k, modifiers);
                return;
            }
            if (article is null || type.GetString() != "preview" ||
                !root.TryGetProperty("position", out var position) || position.ValueKind != JsonValueKind.Number ||
                !position.TryGetDouble(out double seconds) || !double.IsFinite(seconds) || seconds < 0) return;
            NavigationPreview?.Invoke(seconds);
        }
        catch (JsonException) { }
    }

    internal void CancelNavigation()
    {
        navigationEpoch++;
        sentPlayback = null;
        if (ready && !ownerClosing) Post(new { type = "cancel", epoch = navigationEpoch });
    }

    internal void CompleteNavigation(double position, bool playing)
    {
        navigationEpoch++;
        sentPlayback = null;
        if (ready && !ownerClosing) Post(new { type = "commit", epoch = navigationEpoch, position, playing, reset = false });
    }

    internal async void SetAudio(string? path)
    {
        if (ownerClosing || readerFailed || string.Equals(path, audioPath, StringComparison.OrdinalIgnoreCase)) return;
        CancelNavigation();
        audioPath = path;
        scriptLoadCancellation?.Cancel();
        scriptLoadCancellation = null;
        article = null;
        sentPlayback = null;
        message = path is null ? "尚未加载原文" : "正在加载原文…";
        SendArticle();
        if (path is null) return;
        using var cancellation = new CancellationTokenSource();
        scriptLoadCancellation = cancellation;
        ScriptArticle? loaded = null;
        string result;
        try
        {
            loaded = await Task.Run(() => ScriptContent.Find(path, cancellation.Token), cancellation.Token);
            result = loaded is null ? "未找到对应的原文文件" : "";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            result = "原文加载失败：" + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(scriptLoadCancellation, cancellation)) scriptLoadCancellation = null;
        }
        if (ownerClosing || cancellation.IsCancellationRequested) return;
        article = loaded;
        message = result;
        SendArticle();
    }

    internal void SetAppearance(ScriptAppearance value)
    {
        if (appearance == value) return;
        appearance = value;
        if (ready && !ownerClosing) Post(new { type = "appearance", appearance });
    }

    internal void UpdatePlayback(double position, bool playing, bool reset, bool immediate = false)
    {
        playback = (position, playing, reset);
        if (!ready || ownerClosing || !IsVisible || article is null) return;
        if (!immediate && sentPlayback == playback) return;
        sentPlayback = playback;
        Post(new { type = "playback", epoch = navigationEpoch, position, playing, reset, immediate });
    }

    private void Post(object payload)
    {
        try { Reader.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, JsonOptions)); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            if (!ownerClosing) ShowFailure("原文阅读器通信失败：" + ex.Message);
        }
    }

    private void SendArticle()
    {
        if (!ready || ownerClosing) return;
        Post(new { type = "article", article, message, epoch = navigationEpoch });
        sentPlayback = null;
        if (playback is { } current)
            UpdatePlayback(current.Position, current.Playing, current.Reset, immediate: true);
    }

    private void ShowFailure(string text)
    {
        if (ownerClosing || readerFailed) return;
        readerFailed = true;
        ready = false;
        scriptLoadCancellation?.Cancel();
        NavigationCanceled?.Invoke();
        Reader.Visibility = Visibility.Collapsed;
        Status.Text = text;
        Status.Visibility = Visibility.Visible;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (ownerClosing) return;
        e.Cancel = true;
        Hide();
    }

    internal void CloseForOwner()
    {
        ownerClosing = true;
        scriptLoadCancellation?.Cancel();
        scriptLoadCancellation = null;
        Reader.Dispose();
        Close();
    }
}
