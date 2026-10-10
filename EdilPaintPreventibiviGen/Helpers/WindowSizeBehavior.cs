using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Helpers;

/// <summary>Remembers each resizable window's dimensions on this PC, separately from content zoom.</summary>
public static class WindowSizeBehavior
{
    private sealed class SizeState
    {
        public required WindowSizeStore Store;
        public required string Key;
        public bool KeepsOuterSize;
        public bool LastWasMaximized;
        public bool RestoreMaximized;
        public double RequestedWidth, RequestedHeight, AppliedWidth, AppliedHeight;
        public WindowSizePreference? Pending;
        public bool Finished;
    }

    private static readonly ConditionalWeakTable<Window, SizeState> States = new();
    private static WindowSizeStore? _store;

    public static void Initialize(WindowSizeStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

    internal static void RestoreBeforeZoom(Window window, Rect workArea)
    {
        if (_store != null) RestoreBeforeZoom(window, _store, workArea, window is MainWindow);
    }

    internal static void RestoreBeforeZoom(Window window, WindowSizeStore store, Rect workArea, bool keepsOuterSize = false)
    {
        if (States.TryGetValue(window, out _) || !CanRemember(window)) return;
        string key = window.GetType().FullName ?? window.GetType().Name;
        var saved = store.Get(key);
        var state = new SizeState
        {
            Store = store, Key = key, KeepsOuterSize = keepsOuterSize,
            RestoreMaximized = !WindowResizeBehavior.PreventsMaximization(window) &&
                (saved?.IsMaximized ?? window.WindowState == WindowState.Maximized)
        };
        States.Add(window, state);
        if (saved != null)
        {
            window.WindowState = WindowState.Normal;
            window.Width = Math.Clamp(saved.Width, window.MinWidth, window.MaxWidth);
            window.Height = Math.Clamp(saved.Height, window.MinHeight, window.MaxHeight);
        }
        state.RequestedWidth = window.Width;
        state.RequestedHeight = window.Height;
        if (keepsOuterSize)
        {
            // Main-window zoom only affects its content. Fit its normal bounds to this screen.
            double minWidth = Math.Min(window.MinWidth, workArea.Width);
            double minHeight = Math.Min(window.MinHeight, workArea.Height);
            window.MinWidth = minWidth;
            window.MinHeight = minHeight;
            window.Width = Math.Clamp(window.Width, minWidth, Math.Min(window.MaxWidth, workArea.Width));
            window.Height = Math.Clamp(window.Height, minHeight, Math.Min(window.MaxHeight, workArea.Height));
        }
    }

    internal static void FinishRestore(Window window, Rect workArea)
    {
        if (!States.TryGetValue(window, out var state) || state.Finished) return;
        state.Finished = true;
        state.AppliedWidth = window.Width;
        state.AppliedHeight = window.Height;
        if (!state.RestoreMaximized)
        {
            window.WindowState = WindowState.Normal;
            CenterAndFit(window, workArea);
        }
        else window.WindowState = WindowState.Maximized;
        state.LastWasMaximized = window.WindowState == WindowState.Maximized;
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState != WindowState.Minimized)
                state.LastWasMaximized = window.WindowState == WindowState.Maximized;
        };
        // Capture while RestoreBounds still exists, but persist only after a successful close.
        window.Closing += (_, _) => state.Pending = Capture(window, state);
        window.Closed += (_, _) =>
        {
            var preference = state.Pending ?? Capture(window, state);
            if (preference != null && !state.Store.Save(state.Key, preference))
                Debug.WriteLine("[WindowSize] Dimensions retained in memory; local persistence unavailable.");
        };
    }

    internal static bool CanRemember(Window window) =>
        window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip &&
        window.SizeToContent == SizeToContent.Manual &&
        double.IsFinite(window.Width) && window.Width > 0 &&
        double.IsFinite(window.Height) && window.Height > 0;

    internal static WindowSizePreference? Capture(Window window)
        => States.TryGetValue(window, out var state) ? Capture(window, state) : null;

    private static WindowSizePreference? Capture(Window window, SizeState state)
    {
        Size measured;
        var restore = window.RestoreBounds;
        if (window.WindowState != WindowState.Normal && !restore.IsEmpty && restore.Width > 0 && restore.Height > 0)
            measured = restore.Size;
        else measured = new Size(window.Width, window.Height);
        if (!double.IsFinite(measured.Width) || !double.IsFinite(measured.Height) || measured.Width <= 0 || measured.Height <= 0)
            return null;
        Size preferred;
        if (state.KeepsOuterSize)
            preferred = new Size(NearlyEqual(measured.Width, state.AppliedWidth) ? state.RequestedWidth : measured.Width,
                NearlyEqual(measured.Height, state.AppliedHeight) ? state.RequestedHeight : measured.Height);
        else preferred = WindowZoomBehavior.GetPreferredUnscaledSize(window, measured);
        var preference = new WindowSizePreference
        {
            Width = preferred.Width, Height = preferred.Height,
            IsMaximized = !WindowResizeBehavior.PreventsMaximization(window) && state.LastWasMaximized
        };
        return preference.IsValid ? preference : null;
    }

    private static bool NearlyEqual(double left, double right) => double.IsFinite(left) && double.IsFinite(right) &&
        Math.Abs(left - right) < 0.5;

    private static void CenterAndFit(Window window, Rect area)
    {
        if (window.WindowStartupLocation == WindowStartupLocation.CenterOwner && window.Owner is { } owner &&
            double.IsFinite(owner.Left) && double.IsFinite(owner.Top) && owner.ActualWidth > 0 && owner.ActualHeight > 0)
        {
            window.Left = owner.Left + (owner.ActualWidth - window.Width) / 2;
            window.Top = owner.Top + (owner.ActualHeight - window.Height) / 2;
        }
        else if (window.WindowStartupLocation != WindowStartupLocation.Manual)
        {
            window.Left = area.Left + (area.Width - window.Width) / 2;
            window.Top = area.Top + (area.Height - window.Height) / 2;
        }
        if (double.IsFinite(window.Left)) window.Left = Math.Clamp(window.Left, area.Left, Math.Max(area.Left, area.Right - window.Width));
        if (double.IsFinite(window.Top)) window.Top = Math.Clamp(window.Top, area.Top, Math.Max(area.Top, area.Bottom - window.Height));
    }
}
