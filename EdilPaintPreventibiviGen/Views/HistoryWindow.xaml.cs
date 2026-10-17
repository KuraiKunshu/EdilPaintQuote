using System.Windows;
using EdilPaintPreventibiviGen.ViewModels;
namespace EdilPaintPreventibiviGen.Views;

public partial class HistoryWindow : Window
{
    public HistoryView View { get; }
    public HistoryWindow(MainViewModel vm, string? initialSearch = null)
    {
        InitializeComponent();
        View = new HistoryView(vm, initialSearch);
        Content = View;
        View.CloseRequested += (_, _) => Close();
        Closing += (_, e) => { if (!Services.AppShutdownManager.IsShutdownRequested && !View.CanClose) e.Cancel = true; };
        Closed += (_, _) => View.Dispose();
    }
}
