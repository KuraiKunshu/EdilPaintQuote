using EdilPaintPreventibiviGen.Android.Models;

namespace EdilPaintPreventibiviGen.Android;

public partial class QuoteDetailPage : ContentPage
{
    public QuoteDetailPage(QuoteDetail detail)
    {
        InitializeComponent();
        BindingContext = detail;
        CustomerNotesPanel.IsVisible = detail.HasCustomerNotes;
        NotesPanel.IsVisible = detail.HasNotes;
    }
}
