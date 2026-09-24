using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EdilPaintPreventibiviGen.Helpers;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WindowZoomTests
{
    [Fact]
    public void ZoomScalesContentAndConstraintsWithoutAccumulating()
    {
        RunSta(() =>
        {
            var content = new Grid { LayoutTransform = new TranslateTransform(2, 3) };
            var window = new Window
            {
                Content = content, Width = 1000, Height = 700,
                MinWidth = 900, MinHeight = 600, MaxWidth = 1200, MaxHeight = 900
            };
            WindowZoomBehavior.Apply(window, 0.6);
            Assert.Equal(600, window.Width, 6);
            Assert.Equal(420, window.Height, 6);
            Assert.Equal(540, window.MinWidth, 6);
            Assert.Equal(360, window.MinHeight, 6);
            Assert.Equal(720, window.MaxWidth, 6);
            Assert.Equal(540, window.MaxHeight, 6);
            var transforms = Assert.IsType<TransformGroup>(content.LayoutTransform);
            Assert.IsType<TranslateTransform>(transforms.Children[0]);
            Assert.Equal(0.6, Assert.IsType<ScaleTransform>(transforms.Children[1]).ScaleX);

            WindowZoomBehavior.Apply(window, 0.6);
            Assert.Equal(600, window.Width, 6);
            WindowZoomBehavior.Apply(window, 1.3);
            Assert.Equal(1300, window.Width, 6);
            WindowZoomBehavior.Apply(window, 1);
            Assert.Equal(1000, window.Width, 6);
            Assert.Equal(700, window.Height, 6);
            Assert.Equal(900, window.MinWidth, 6);
            Assert.Equal(1200, window.MaxWidth, 6);
            Assert.Equal(2, transforms.Children.Count);
            window.Close();
        });
    }

    [Fact]
    public void ZoomFitsWorkingAreaAndRestoresUnclippedDimensions()
    {
        RunSta(() =>
        {
            var window = new Window { Content = new Grid(), Width = 1000, Height = 840,
                MinWidth = 900, MinHeight = 640, Left = 1700, Top = 500 };
            var area = new Rect(100, 0, 1266, 720);
            WindowZoomBehavior.Apply(window, 1.3, area);
            Assert.Equal(1266, window.Width);
            Assert.Equal(720, window.Height);
            Assert.Equal(720, window.MinHeight);
            Assert.Equal(100, window.Left);
            Assert.Equal(0, window.Top);
            WindowZoomBehavior.Apply(window, 0.6, area);
            Assert.Equal(600, window.Width, 6);
            Assert.Equal(504, window.Height, 6);
            Assert.Equal(384, window.MinHeight, 6);
            window.Width = 650;
            WindowZoomBehavior.Apply(window, 1, new Rect(0, 0, 1920, 1080));
            Assert.Equal(650 / 0.6, window.Width, 6);
            Assert.Equal(840, window.Height, 6);
            window.Close();
        });
    }

    [Fact]
    public void AutoSizedWindowsKeepAutomaticDimensions()
    {
        RunSta(() =>
        {
            var window = new Window { Content = new Grid(), SizeToContent = SizeToContent.WidthAndHeight };
            WindowZoomBehavior.Apply(window, 0.8);
            Assert.True(double.IsNaN(window.Width));
            Assert.True(double.IsNaN(window.Height));
            Assert.Equal(double.PositiveInfinity, window.MaxWidth);
            Assert.Equal(SizeToContent.WidthAndHeight, window.SizeToContent);
            window.Close();
        });
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }
}
