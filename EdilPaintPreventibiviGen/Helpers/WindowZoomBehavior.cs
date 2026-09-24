using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace EdilPaintPreventibiviGen.Helpers;

/// <summary>Shares the main window zoom with every WPF application window.</summary>
public static class WindowZoomBehavior
{
    private sealed class ZoomState
    {
        public double Scale = 1;
        public ScaleTransform Transform { get; } = new();
        public double MinWidth, MinHeight, MaxWidth, MaxHeight;
        public double RequestedWidth, RequestedHeight, LastWidth, LastHeight;
    }

    private static readonly ConditionalWeakTable<Window, ZoomState> States = new();
    private static double _scale = 1;
    private static bool _initialized;

    public static void Initialize(double scale)
    {
        if (!_initialized)
        {
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded));
            _initialized = true;
        }
        SetScale(scale);
    }

    public static void SetScale(double scale)
    {
        _scale = double.IsFinite(scale) ? Math.Clamp(scale, 0.6, 1.3) : 1;
        if (Application.Current == null)
            return;

        foreach (Window window in Application.Current.Windows)
            if (window.IsLoaded)
                Apply(window, _scale, GetWorkArea(window));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // Ignore any descendant event: only the window owns this scale.
        if (ReferenceEquals(sender, e.OriginalSource))
            Apply((Window)sender, _scale, GetWorkArea((Window)sender));
    }

    internal static void Apply(Window window, double scale, Rect? workArea = null)
    {
        // The main window already scales its own content and keeps its outer size.
        if (window is MainWindow || window.Content is not FrameworkElement content)
            return;

        if (!States.TryGetValue(window, out var state))
        {
            state = new ZoomState
            {
                MinWidth = window.MinWidth, MinHeight = window.MinHeight,
                MaxWidth = window.MaxWidth, MaxHeight = window.MaxHeight,
                RequestedWidth = window.Width, RequestedHeight = window.Height,
                LastWidth = window.Width, LastHeight = window.Height
            };
            var transforms = new TransformGroup();
            transforms.Children.Add(content.LayoutTransform);
            transforms.Children.Add(state.Transform);
            content.LayoutTransform = transforms;
            States.Add(window, state);
        }

        double ratio = scale / state.Scale;
        // Preserve the intended dimensions when a monitor has capped the previous size,
        // but respect any manual resize performed since the last zoom change.
        double width = (window.Width.Equals(state.LastWidth) ? state.RequestedWidth : window.Width) * ratio;
        double height = (window.Height.Equals(state.LastHeight) ? state.RequestedHeight : window.Height) * ratio;
        state.RequestedWidth = width;
        state.RequestedHeight = height;
        double availableWidth = workArea?.Width ?? double.PositiveInfinity;
        double availableHeight = workArea?.Height ?? double.PositiveInfinity;
        double minWidth = Math.Min(state.MinWidth * scale, availableWidth);
        double minHeight = Math.Min(state.MinHeight * scale, availableHeight);
        double maxWidth = Math.Min(state.MaxWidth * scale, availableWidth);
        double maxHeight = Math.Min(state.MaxHeight * scale, availableHeight);
        width = Math.Min(width, maxWidth);
        height = Math.Min(height, maxHeight);

        // Release the old constraints before assigning the scaled dimensions.
        window.MinWidth = 0;
        window.MinHeight = 0;
        window.MaxWidth = double.PositiveInfinity;
        window.MaxHeight = double.PositiveInfinity;
        window.Width = width;
        window.Height = height;
        window.MaxWidth = maxWidth;
        window.MaxHeight = maxHeight;
        window.MinWidth = minWidth;
        window.MinHeight = minHeight;
        state.Transform.ScaleX = scale;
        state.Transform.ScaleY = scale;
        state.Scale = scale;
        state.LastWidth = window.Width;
        state.LastHeight = window.Height;
        if (workArea is { } area && window.WindowState == WindowState.Normal)
        {
            if (double.IsFinite(window.Left) && double.IsFinite(window.Width))
                window.Left = Math.Clamp(window.Left, area.Left, Math.Max(area.Left, area.Right - window.Width));
            if (double.IsFinite(window.Top) && double.IsFinite(window.Height))
                window.Top = Math.Clamp(window.Top, area.Top, Math.Max(area.Top, area.Bottom - window.Height));
        }
    }

    private static Rect GetWorkArea(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (handle != 0 && GetMonitorInfo(MonitorFromWindow(handle, 2), ref info))
        {
            var fromDevice = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var topLeft = fromDevice.Transform(new Point(info.Work.Left, info.Work.Top));
            var bottomRight = fromDevice.Transform(new Point(info.Work.Right, info.Work.Bottom));
            return new Rect(topLeft, bottomRight);
        }
        return SystemParameters.WorkArea;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
