using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace AudioPlayer.Script;

// Owns the companion window and its native owner integration. Playback state
// and navigation transactions remain with the caller.
internal sealed class ScriptWindowHost : IDisposable
{
    private readonly Window owner;
    private readonly Func<ScriptWindow> createReader;
    private ScriptWindow? reader;
    private HwndSource? source;
    private bool disposed;

    public ScriptWindowHost(Window owner, Func<ScriptWindow> createReader)
    {
        this.owner = owner;
        this.createReader = createReader;
        owner.SourceInitialized += OwnerSourceInitialized;
        owner.LocationChanged += OwnerLocationChanged;
        owner.SizeChanged += OwnerSizeChanged;
        owner.StateChanged += OwnerStateChanged;
    }

    public bool HasWindow => reader is not null;
    public bool IsVisible => reader?.IsVisible == true;
    public event Action? VisibilityChanged;
    public event Action<double>? NavigationPreview;
    public event Action? NavigationCanceled;
    public event Action<Key, ModifierKeys>? Shortcut;

    // True only when the reader was shown; the caller then reconciles playback.
    public bool Toggle(ScriptAppearance appearance)
    {
        if (disposed || owner.WindowState == WindowState.Minimized) return false;
        if (reader is { IsVisible: false, ReaderFailure: not null })
            CloseWindow();
        if (reader is null)
        {
            var window = createReader();
            window.Owner = owner;
            window.Height = Math.Max(300, owner.ActualHeight);
            reader = window;
            window.SetAppearance(appearance);
            window.NavigationPreview += seconds =>
            {
                if (ReferenceEquals(reader, window)) NavigationPreview?.Invoke(seconds);
            };
            window.NavigationCanceled += () =>
            {
                if (ReferenceEquals(reader, window)) NavigationCanceled?.Invoke();
            };
            window.Shortcut += (key, modifiers) =>
            {
                if (ReferenceEquals(reader, window)) Shortcut?.Invoke(key, modifiers);
            };
            window.IsVisibleChanged += (_, _) =>
            {
                if (!ReferenceEquals(reader, window)) return;
                VisibilityChanged?.Invoke();
                if (!window.IsVisible) NavigationCanceled?.Invoke();
            };
        }
        if (reader.IsVisible)
        {
            reader.Hide();
            return false;
        }
        PositionWindow();
        reader.Show();
        return true;
    }

    public void SetAppearance(ScriptAppearance appearance) => reader?.SetAppearance(appearance);
    public void SetAudio(string? path) => reader?.SetAudio(path);
    public void UpdatePlayback(double position, bool playing, bool reset,
        bool reconcile = false, double? committedTarget = null)
        => reader?.UpdatePlayback(position, playing, reset, reconcile, committedTarget);

    private void OwnerSourceInitialized(object? sender, EventArgs e)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(owner).Handle);
        source?.AddHook(OwnerMessage);
    }

    private IntPtr OwnerMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Close the reader before Windows handles native SC_MINIMIZE.
        if (message == 0x0112 && (wParam.ToInt64() & 0xfff0) == 0xf020)
            CloseForMinimize();
        return IntPtr.Zero;
    }

    private void OwnerLocationChanged(object? sender, EventArgs e) => PositionWindow();
    private void OwnerSizeChanged(object sender, SizeChangedEventArgs e) => PositionWindow();

    private void OwnerStateChanged(object? sender, EventArgs e)
    {
        // Fallback for programmatic minimize or shell paths that bypass SC_MINIMIZE.
        if (owner.WindowState == WindowState.Minimized) CloseForMinimize();
        else PositionWindow();
    }

    private void CloseForMinimize()
    {
        if (disposed || reader is null) return;
        CloseWindow();
        NavigationCanceled?.Invoke();
    }

    private void CloseWindow()
    {
        var window = reader;
        reader = null;
        window?.CloseForOwner();
        VisibilityChanged?.Invoke();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        owner.SourceInitialized -= OwnerSourceInitialized;
        owner.LocationChanged -= OwnerLocationChanged;
        owner.SizeChanged -= OwnerSizeChanged;
        owner.StateChanged -= OwnerStateChanged;
        source?.RemoveHook(OwnerMessage);
        source = null;
        // Owner shutdown must not resume a pending navigation preview.
        CloseWindow();
    }

    private void PositionWindow()
    {
        if (reader is null || disposed || owner.WindowState == WindowState.Minimized) return;
        Rect area = SystemParameters.WorkArea;
        var source = PresentationSource.FromVisual(owner);
        if (source?.CompositionTarget is { } target)
        {
            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(owner).Handle;
            var info = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(MonitorFromWindow(handle, 2), ref info))
            {
                var scale = target.TransformFromDevice;
                area = new Rect(scale.Transform(new Point(info.Work.Left, info.Work.Top)),
                    scale.Transform(new Point(info.Work.Right, info.Work.Bottom)));
            }
        }
        double width = reader.Width;
        double height = reader.Height;
        double right = owner.WindowState == WindowState.Maximized ? area.Right : owner.Left + owner.ActualWidth;
        reader.Left = Math.Clamp(right, area.Left, Math.Max(area.Left, area.Right - width));
        reader.Top = Math.Clamp(owner.Top, area.Top, Math.Max(area.Top, area.Bottom - height));
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

}
