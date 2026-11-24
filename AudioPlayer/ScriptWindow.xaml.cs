using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace AudioPlayer;

public partial class ScriptWindow : Window
{
    private bool ownerClosing;
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
    private int generation;
    private ScriptArticle? article;
    private string message = "尚未加载原文";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ScriptWindow()
    {
        InitializeComponent();
        Loaded += InitializeReader;
    }

    private async void InitializeReader(object sender, RoutedEventArgs e)
    {
        if (initialized) return;
        initialized = true;
        try
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "cache", "webview2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: folder);
            if (ownerClosing) return;
            await Reader.EnsureCoreWebView2Async(environment);
            if (ownerClosing) return;
            var core = Reader.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NavigationStarting += (_, args) =>
            {
                if (args.Uri != "about:blank") args.Cancel = true;
            };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.ProcessFailed += (_, _) => ShowFailure("WebView2 进程异常，请重新启动程序。");
            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess) ShowFailure("原文页面加载失败。");
            };
            core.WebMessageReceived += (_, args) =>
            {
                if (ownerClosing || args.Source != "about:blank") return;
                if (args.WebMessageAsJson == "\"ready\"")
                {
                    ready = true;
                    Status.Visibility = Visibility.Collapsed;
                    Reader.Visibility = Visibility.Visible;
                    Post(new { type = "appearance", appearance });
                    SendArticle();
                    return;
                }
                ReceiveNavigation(args.WebMessageAsJson);
            };
            using var stream = typeof(ScriptWindow).Assembly.GetManifestResourceStream("AudioPlayer.web.script.html")
                ?? throw new InvalidDataException("缺少原文阅读资源。");
            using var reader = new StreamReader(stream);
            core.NavigateToString(reader.ReadToEnd());
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
                !root.TryGetProperty("epoch", out var epoch) || !epoch.TryGetInt32(out int id) || id != navigationEpoch ||
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
        catch (InvalidOperationException) { }
    }

    internal void CancelNavigation()
    {
        navigationEpoch++;
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
        if (ownerClosing || string.Equals(path, audioPath, StringComparison.OrdinalIgnoreCase)) return;
        CancelNavigation();
        audioPath = path;
        int request = ++generation;
        article = null;
        sentPlayback = null;
        message = path is null ? "尚未加载原文" : "正在加载原文…";
        SendArticle();
        if (path is null) return;
        ScriptArticle? loaded = null;
        string result;
        try
        {
            loaded = await Task.Run(() => ScriptContent.Find(path));
            result = loaded is null ? "未找到对应的原文文件" : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            result = "原文加载失败：" + ex.Message;
        }
        if (ownerClosing || request != generation) return;
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
        Post(new { type = "playback", position, playing, reset, immediate });
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
        ready = false;
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
        generation++;
        Reader.Dispose();
        Close();
    }
}
