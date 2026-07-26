namespace EdilPaintPreventibiviGen.Android;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new NavigationPage(new MainPage())
        {
            BarBackgroundColor = Color.FromArgb("#B3261E"),
            BarTextColor = Colors.White
        });
    }
}
