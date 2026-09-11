namespace EdilPaintPreventibiviGen.Android;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        ContentPage page = new MainPage();
#if MOBILE_SMOKE_TEST
        page = new Diagnostics.SmokeTestPage();
#endif
        return new Window(new NavigationPage(page)
        {
            BarBackgroundColor = Color.FromArgb("#B3261E"),
            BarTextColor = Colors.White
        });
    }
}
