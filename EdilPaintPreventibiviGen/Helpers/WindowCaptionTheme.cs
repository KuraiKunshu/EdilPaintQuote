using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace EdilPaintPreventibiviGen.Helpers;

/// <summary>Keeps native title bars consistent with the app instead of the Windows accent color.</summary>
public static class WindowCaptionTheme
{
    private static bool _initialized;
    private const int UseImmersiveDarkMode = 20;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, e.OriginalSource)) _ = Apply((Window)sender);
    }

    internal static bool Apply(Window window)
    {
        // Caption colors are supported on Windows 11; keep accessibility/system defaults elsewhere.
        if (window.WindowStyle == WindowStyle.None || SystemParameters.HighContrast ||
            !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return false;
        nint handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return false;
        int lightMode = 0;
        int background = GetColor(window, "WhiteBrush", Colors.White);
        int foreground = GetColor(window, "DefaultTextBrush", Colors.Black);
        // A refused cosmetic attribute must not interfere with opening the window.
        _ = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref lightMode, sizeof(int));
        int backgroundResult = DwmSetWindowAttribute(handle, CaptionColor, ref background, sizeof(int));
        int foregroundResult = DwmSetWindowAttribute(handle, TextColor, ref foreground, sizeof(int));
        return backgroundResult == 0 && foregroundResult == 0;
    }

    private static int GetColor(Window window, string resource, Color fallback)
    {
        var color = window.TryFindResource(resource) is SolidColorBrush brush ? brush.Color : fallback;
        return color.R | color.G << 8 | color.B << 16;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint handle, int attribute, ref int value, int size);
}
