using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EdilPaintPreventibiviGen.Helpers;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WindowSizeBehaviorTests
{
    private static readonly Rect Area = new(-1920, 40, 1920, 1040);

    [Fact]
    public void ResizeIsRestoredAtTheSameZoomWithoutAccumulating()
    {
        RunSta(store =>
        {
            var first = Open(new RememberedWindow(), store, 0.6);
            first.Width = 650;
            first.Height = 470;
            first.Close();
            var saved = store.Get(typeof(RememberedWindow).FullName!)!;
            Assert.Equal(650 / 0.6, saved.Width, 6);
            Assert.Equal(470 / 0.6, saved.Height, 6);
            for (int i = 0; i < 3; i++)
            {
                var reopened = Open(new RememberedWindow(), store, 0.6);
                Assert.Equal(650, reopened.Width, 6);
                Assert.Equal(470, reopened.Height, 6);
                reopened.Close();
            }
            var larger = Open(new RememberedWindow(), store, 1.3);
            Assert.Equal(650 / 0.6 * 1.3, larger.Width, 6);
            Assert.Equal(470 / 0.6 * 1.3, larger.Height, 6);
            larger.Close();
            var standard = Open(new RememberedWindow(), store, 1);
            Assert.Equal(saved.Width, standard.Width, 6);
            Assert.Equal(saved.Height, standard.Height, 6);
            standard.Close();
        });
    }

    [Fact]
    public void AClippedMonitorDoesNotPermanentlyShrinkTheSavedSize()
    {
        RunSta(store =>
        {
            store.Save(typeof(RememberedWindow).FullName!, new() { Width = 1300, Height = 900 });
            var small = Open(new RememberedWindow(), store, 1.3, new Rect(-800, 0, 800, 600));
            Assert.Equal(800, small.Width);
            Assert.Equal(600, small.Height);
            Assert.InRange(small.Left, -800, 0);
            small.Close();
            Assert.Equal(1300, store.Get(typeof(RememberedWindow).FullName!)!.Width);
            Assert.Equal(900, store.Get(typeof(RememberedWindow).FullName!)!.Height);
            var large = Open(new RememberedWindow(), store, 1);
            Assert.Equal(1300, large.Width);
            Assert.Equal(900, large.Height);
            large.Close();
        });
    }

    [Fact]
    public void AManualResizeAfterMonitorClippingReplacesTheSavedSize()
    {
        RunSta(store =>
        {
            store.Save(typeof(RememberedWindow).FullName!, new() { Width = 1300, Height = 900 });
            var window = Open(new RememberedWindow(), store, 0.8, new Rect(0, 0, 800, 600));
            window.Width = 720;
            window.Height = 520;
            window.Close();
            var saved = store.Get(typeof(RememberedWindow).FullName!)!;
            Assert.Equal(900, saved.Width);
            Assert.Equal(650, saved.Height);
        });
    }

    [Fact]
    public void WindowsRememberTheirSizesIndependentlyByType()
    {
        RunSta(store =>
        {
            var first = Open(new RememberedWindow(), store, 1);
            var second = Open(new OtherWindow(), store, 1);
            first.Width = 1300; first.Height = 850;
            second.Width = 750; second.Height = 500;
            first.Close(); second.Close();
            var reopenedFirst = Open(new RememberedWindow(), store, 1);
            var reopenedSecond = Open(new OtherWindow(), store, 1);
            Assert.Equal(1300, reopenedFirst.Width);
            Assert.Equal(850, reopenedFirst.Height);
            Assert.Equal(750, reopenedSecond.Width);
            Assert.Equal(500, reopenedSecond.Height);
            reopenedFirst.Close(); reopenedSecond.Close();
        });
    }

    [Fact]
    public void CancelledCloseDoesNotPersistUntilTheWindowActuallyCloses()
    {
        RunSta(store =>
        {
            var window = Open(new RememberedWindow(), store, 1);
            bool cancel = true;
            window.Closing += (_, e) => e.Cancel = cancel;
            window.Width = 1200;
            window.Close();
            Assert.Null(store.Get(typeof(RememberedWindow).FullName!));
            window.Width = 1400;
            cancel = false;
            window.Close();
            Assert.Equal(1400, store.Get(typeof(RememberedWindow).FullName!)!.Width);
        });
    }

    [Theory]
    [InlineData(ResizeMode.NoResize, SizeToContent.Manual)]
    [InlineData(ResizeMode.CanMinimize, SizeToContent.Manual)]
    [InlineData(ResizeMode.CanResize, SizeToContent.WidthAndHeight)]
    public void FixedAndAutoSizedWindowsKeepTheirDeclaredBehavior(ResizeMode resizeMode, SizeToContent sizeToContent)
    {
        RunSta(store =>
        {
            var window = new RememberedWindow { ResizeMode = resizeMode, SizeToContent = sizeToContent };
            store.Save(typeof(RememberedWindow).FullName!, new() { Width = 1400, Height = 900 });
            WindowSizeBehavior.RestoreBeforeZoom(window, store, Area);
            WindowSizeBehavior.FinishRestore(window, Area);
            Assert.Equal(1000, window.Width);
            Assert.Equal(700, window.Height);
            Assert.Null(WindowSizeBehavior.Capture(window));
            window.Close();
        });
    }

    [Fact]
    public void SavedSizesRespectNewWindowConstraints()
    {
        RunSta(store =>
        {
            store.Save(typeof(RememberedWindow).FullName!, new() { Width = 3000, Height = 100 });
            var window = Open(new RememberedWindow(), store, 0.8);
            Assert.Equal(1280, window.Width);
            Assert.Equal(320, window.Height);
            window.Close();
        });
    }

    [Fact]
    public void MainOuterSizeIsNotDividedByContentZoomAndRetainsRequestedSizeOnSmallScreens()
    {
        RunSta(store =>
        {
            store.Save(typeof(RememberedWindow).FullName!, new() { Width = 1300, Height = 900 });
            var small = new RememberedWindow();
            var smallArea = new Rect(0, 0, 800, 600);
            WindowSizeBehavior.RestoreBeforeZoom(small, store, smallArea, keepsOuterSize: true);
            WindowSizeBehavior.FinishRestore(small, smallArea);
            Assert.Equal(800, small.Width);
            Assert.Equal(600, small.Height);
            small.Close();
            Assert.Equal(1300, store.Get(typeof(RememberedWindow).FullName!)!.Width);
            var large = new RememberedWindow();
            WindowSizeBehavior.RestoreBeforeZoom(large, store, Area, keepsOuterSize: true);
            WindowSizeBehavior.FinishRestore(large, Area);
            large.Width = 1200;
            large.Close();
            Assert.Equal(1200, store.Get(typeof(RememberedWindow).FullName!)!.Width);
        });
    }

    [Fact]
    public void MinimizedWindowsRetainTheirLastUsefulMaximizedState()
    {
        RunSta(store =>
        {
            var window = Open(new RememberedWindow(), store, 1);
            // WPF raises StateChanged only for a window with a native handle.
            // Exercise that lifecycle without displaying a test window to the user.
            window.Opacity = 0;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            try
            {
                window.Show();
                window.WindowState = WindowState.Maximized;
                window.WindowState = WindowState.Minimized;
            }
            finally { window.Close(); }
            Assert.True(store.Get(typeof(RememberedWindow).FullName!)!.IsMaximized);
            var reopened = Open(new RememberedWindow(), store, 1);
            Assert.Equal(WindowState.Maximized, reopened.WindowState);
            reopened.Close();
        });
    }

    [Fact]
    public void LoadedWindowRestoresSizeAndZoomAcrossNewStoreInstances()
    {
        RunSta((store, storePath) =>
        {
            string key = typeof(LoadedWindow).FullName!;
            store.Save(key, new() { Width = 900, Height = 650 });
            WindowSizeBehavior.Initialize(store);
            WindowZoomBehavior.Initialize(0.8);
            var first = new LoadedWindow();
            try
            {
                first.Show();
                first.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(first.IsLoaded);
                Assert.Equal(720, first.Width, 6);
                Assert.Equal(520, first.Height, 6);
                first.Width = 760;
                first.Height = 560;
            }
            finally { first.Close(); }

            // Simulate a fresh session: read the file through another store instance.
            WindowSizeBehavior.Initialize(new WindowSizeStore(storePath));
            var reopened = new LoadedWindow();
            try
            {
                reopened.Show();
                reopened.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Equal(760, reopened.Width, 6);
                Assert.Equal(560, reopened.Height, 6);
            }
            finally { reopened.Close(); }
        });
    }

    [Fact]
    public void WindowsWhichPreventMaximizationDoNotRestoreMaximizedPreferences()
    {
        RunSta(store =>
        {
            store.Save(typeof(RememberedWindow).FullName!, new() { Width = 1200, Height = 800, IsMaximized = true });
            var window = new RememberedWindow();
            WindowResizeBehavior.PreventMaximizedState(window);
            Open(window, store, 1);
            Assert.Equal(WindowState.Normal, window.WindowState);
            window.Close();
            Assert.False(store.Get(typeof(RememberedWindow).FullName!)!.IsMaximized);
        });
    }

    private static Window Open(Window window, WindowSizeStore store, double scale, Rect? area = null)
    {
        var workArea = area ?? Area;
        WindowSizeBehavior.RestoreBeforeZoom(window, store, workArea);
        WindowZoomBehavior.Apply(window, scale, workArea);
        WindowSizeBehavior.FinishRestore(window, workArea);
        return window;
    }

    private class RememberedWindow : Window
    {
        public RememberedWindow()
        {
            Content = new Grid(); Width = 1000; Height = 700;
            MinWidth = 600; MinHeight = 400; MaxWidth = 1600; MaxHeight = 1400;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }
    private sealed class OtherWindow : RememberedWindow { }
    private sealed class LoadedWindow : RememberedWindow
    {
        public LoadedWindow()
        {
            Opacity = 0;
            ShowActivated = false;
            ShowInTaskbar = false;
        }
    }

    private static void RunSta(Action<WindowSizeStore> action)
        => RunSta((store, _) => action(store));

    private static void RunSta(Action<WindowSizeStore, string> action)
    {
        Exception? error = null;
        var path = Path.Combine(Path.GetTempPath(), "EdilPaint-window-size-test", Guid.NewGuid().ToString("N"));
        var thread = new Thread(() =>
        {
            string storePath = Path.Combine(path, "sizes.json");
            try { action(new WindowSizeStore(storePath), storePath); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
