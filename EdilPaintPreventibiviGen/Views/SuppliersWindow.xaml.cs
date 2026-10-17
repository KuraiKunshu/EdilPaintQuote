using System.Windows;
using EdilPaintPreventibiviGen.ViewModels;
namespace EdilPaintPreventibiviGen.Views;

public partial class SuppliersWindow : Window
{
    public SupplierOrdersView View { get; }
    public SuppliersWindow(MainViewModel vm)
    {
        InitializeComponent();
        View = new SupplierOrdersView(vm);
        Content = View;
        View.CloseRequested += (_, _) => Close();
        Activated += async (_, _) => await View.RefreshCurrentAsync();
        Closing += (_, e) => { if (!Services.AppShutdownManager.IsShutdownRequested && !View.CanClose) e.Cancel = true; };
        Closed += (_, _) => View.Dispose();
    }
}
