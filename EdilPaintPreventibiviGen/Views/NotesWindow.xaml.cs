using System.Windows;
using System.Windows.Input;

namespace EdilPaintPreventibiviGen.Views;

public partial class NotesWindow : Window
{
    public string ResultNotes { get; private set; }

    public NotesWindow(string initialNotes)
    {
        InitializeComponent();

        TxtNotes.Text = initialNotes ?? string.Empty;
        ResultNotes = string.Empty;

        Loaded += NotesWindow_Loaded;
        PreviewKeyDown += NotesWindow_PreviewKeyDown;
    }

    private void NotesWindow_Loaded(object sender, RoutedEventArgs e)
    {
        TxtNotes.Focus();
        TxtNotes.CaretIndex = TxtNotes.Text.Length;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ResultNotes = TxtNotes.Text;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void NotesWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }
}