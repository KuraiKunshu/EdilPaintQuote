using System.Globalization;
using EdilPaintPreventibiviGen.Android.Services;

namespace EdilPaintPreventibiviGen.Android.Controls;

public class OperationPage : ContentPage
{
    protected readonly VerticalStackLayout Form = new() { Padding = 16, Spacing = 12 };
    protected readonly MobileDatabaseService Database = new();
    protected readonly string ConnectionString;
    private readonly ActivityIndicator _busy = new() { Color = Color.FromArgb("#B3261E"), IsVisible = false };
    private bool _running;

    public OperationPage(string connectionString, string title)
    {
        ConnectionString = connectionString;
        Title = title;
        BackgroundColor = Color.FromArgb("#F7F7F8");
        var grid = new Grid();
        grid.Children.Add(new ScrollView { Content = Form });
        grid.Children.Add(_busy);
        Content = grid;
    }

    protected async Task RunAsync(Func<Task> action)
    {
        if (_running) return;
        _running = true;
        Form.IsEnabled = false;
        _busy.IsVisible = _busy.IsRunning = true;
        try { await action(); }
        catch (Exception exception)
        {
            await DisplayAlertAsync(Title ?? "Operazione", MobileDatabaseService.GetUserMessage(exception), "OK");
        }
        finally
        {
            _running = false;
            Form.IsEnabled = true;
            _busy.IsVisible = _busy.IsRunning = false;
        }
    }

    protected Button Action(string title, Func<Task> action, bool secondary = false)
    {
        var button = new Button
        {
            Text = title, CornerRadius = 8, MinimumHeightRequest = 48,
            BackgroundColor = Color.FromArgb(secondary ? "#FFFFFF" : "#B3261E"),
            TextColor = Color.FromArgb(secondary ? "#B3261E" : "#FFFFFF"),
            BorderWidth = secondary ? 1 : 0, BorderColor = Color.FromArgb("#DDDDDF")
        };
        button.Clicked += async (_, _) => await RunAsync(action);
        return button;
    }

    protected static Label Heading(string text) => new()
    {
        Text = text, FontSize = 18, FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#202124"), Margin = new Thickness(0, 8, 0, 0)
    };

    protected static View Field(string title, View input) => new VerticalStackLayout
    {
        Spacing = 3,
        Children = { new Label { Text = title, FontSize = 12, TextColor = Color.FromArgb("#616166") }, input }
    };

    protected static Entry Input(string? text = "", bool numeric = false) => new()
    {
        Text = text, Keyboard = numeric ? Keyboard.Numeric : Keyboard.Default,
        BackgroundColor = Colors.White, TextColor = Color.FromArgb("#202124"), MinimumHeightRequest = 48
    };

    protected static View Toggle(string title, Switch control)
    {
        var row = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 8 };
        row.Add(control);
        row.Add(new Label { Text = title, VerticalOptions = LayoutOptions.Center, FontSize = 13, LineBreakMode = LineBreakMode.WordWrap }, 1);
        return row;
    }

    protected static double Number(Entry entry, string title, double maximum = double.MaxValue)
    {
        string value = (entry.Text ?? "").Trim().Replace(',', '.');
        if (!double.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number) || number < 0 || number > maximum)
            throw new InvalidOperationException($"{title}: inserisci un numero tra 0 e {maximum:0.##}.");
        return number;
    }

    protected static int WholeNumber(Entry entry, string title)
    {
        double value = Number(entry, title, int.MaxValue);
        if (value != Math.Truncate(value)) throw new InvalidOperationException($"{title}: inserisci un numero intero.");
        return (int)value;
    }

    protected static string Format(double value) => value.ToString("0.##", CultureInfo.GetCultureInfo("it-IT"));
}
