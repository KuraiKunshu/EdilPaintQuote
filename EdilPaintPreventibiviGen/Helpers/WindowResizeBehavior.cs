using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace EdilPaintPreventibiviGen.Helpers;

public static class WindowResizeBehavior
{
    private const int GwlStyle = -16;
    private const int WsMaximizeBox = 0x00010000;

    public static void PreventMaximizedState(Window window)
    {
        window.SourceInitialized += (_, _) => DisableMaximizeBox(window);
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState != WindowState.Maximized)
                return;

            window.Dispatcher.BeginInvoke(() =>
            {
                if (window.WindowState == WindowState.Maximized)
                    window.WindowState = WindowState.Normal;
            }, DispatcherPriority.Send);
        };
    }

    private static void DisableMaximizeBox(Window window)
    {
        nint handle = new WindowInteropHelper(window).Handle;
        if (handle == 0)
            return;

        nint style = GetWindowLongPtr(handle, GwlStyle);
        SetWindowLongPtr(handle, GwlStyle, style & ~WsMaximizeBox);
    }

    private static nint GetWindowLongPtr(nint handle, int index) =>
        nint.Size == 8
            ? GetWindowLongPtr64(handle, index)
            : GetWindowLong32(handle, index);

    private static nint SetWindowLongPtr(nint handle, int index, nint value) =>
        nint.Size == 8
            ? SetWindowLongPtr64(handle, index, value)
            : SetWindowLong32(handle, index, value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern nint GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern nint SetWindowLong32(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);
}
