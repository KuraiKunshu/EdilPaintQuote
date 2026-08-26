using EdilPaintPreventibiviGen.Android.Models;

namespace EdilPaintPreventibiviGen.Android;

public partial class QuoteDetailPage : ContentPage
{
    private readonly string _connectionString;
    private readonly QuoteDetail _detail;

    public QuoteDetailPage(string connectionString, QuoteDetail detail)
    {
        InitializeComponent();
        _connectionString = connectionString;
        _detail = detail;
        BindingContext = detail;
        CustomerNotesPanel.IsVisible = detail.HasCustomerNotes;
        NotesPanel.IsVisible = detail.HasNotes;
    }

    private async void OnEditClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new QuoteEditorPage(_connectionString, _detail));
}
