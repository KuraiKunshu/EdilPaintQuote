using EdilPaintPreventibiviGen.Android.Models;

namespace EdilPaintPreventibiviGen.Android;

public partial class QuoteDetailPage : ContentPage
{
    public QuoteDetailPage(QuoteDetail detail)
    {
        InitializeComponent();
        BindingContext = detail;
        NotesPanel.IsVisible = !string.IsNullOrWhiteSpace(detail.Notes);
    }
}
