using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace AudioPlayer.Script;

public partial class ScriptWindow : Window
{
    private const string ReaderOrigin = "https://audioplayer.invalid";
    private const string ReaderDocument = ReaderOrigin + "/script.html";
    private readonly string cacheDirectory;
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
    private string? audioPath;
    private CancellationTokenSource? scriptLoadCancellation;
    private ScriptArticle? article;
    private string message = "尚未加载原文";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal ScriptWindow(string cacheDirectory)
    {
        this.cacheDirectory = cacheDirectory;
        InitializeComponent();
        Loaded += InitializeReader;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Keep native resizing and Close, but remove both caption size buttons.
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        const int GwlStyle = -16, WsMinimizeBox = 0x00020000, WsMaximizeBox = 0x00010000;
        SetWindowLong(handle, GwlStyle, GetWindowLong(handle, GwlStyle) & ~(WsMinimizeBox | WsMaximizeBox));
        // SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr handle, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    internal bool ReaderReady => ready;
    internal string? ReaderFailure => readerFailed ? Status.Text : null;

    private async void InitializeReader(object sender, RoutedEventArgs e)
    {
        if (initialized) return;
        initialized = true;
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: cacheDirectory);
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
                navigationEpoch++;
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

    internal async void SetAudio(string? path)
    {
        if (ownerClosing || readerFailed || string.Equals(path, audioPath, StringComparison.OrdinalIgnoreCase)) return;
        navigationEpoch++;
        playback = null; // Never send the previous track's time with a new article.
        audioPath = path;
        scriptLoadCancellation?.Cancel();
        scriptLoadCancellation = null;
        article = null;
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

    // One authoritative snapshot protocol. Reconciliation also invalidates
    // queued requests from the previous navigation epoch.
    internal void UpdatePlayback(double position, bool playing, bool reset, bool reconcile = false, double? committedTarget = null)
    {
        var current = (Position: position, Playing: playing, Reset: reset);
        bool changed = playback != current;
        playback = current;
        if (reconcile) navigationEpoch++;
        if (!ready || ownerClosing || !IsVisible || (!reconcile && !changed)) return;
        Post(new { type = "playback", epoch = navigationEpoch, position, playing, reset, committedTarget });
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
        if (playback is { } current)
            UpdatePlayback(current.Position, current.Playing, current.Reset, reconcile: true);
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
        if (ownerClosing) return;
        ownerClosing = true;
        scriptLoadCancellation?.Cancel();
        scriptLoadCancellation = null;
        if (IsVisible) Hide();
        try { Reader.Dispose(); }
        finally { Close(); }
    }
}
