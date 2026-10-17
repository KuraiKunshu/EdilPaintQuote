using System.Windows;
using EdilPaintPreventibiviGen.ViewModels;
namespace EdilPaintPreventibiviGen.Views;

public partial class WorkScheduleWindow : Window
{
    public WorkScheduleView View { get; }
    public WorkScheduleWindow()
    {
        InitializeComponent();
        View = new WorkScheduleView();
        Content = View;
        View.CloseRequested += (_, _) => Close();
        Closing += (_, e) => { if (!Services.AppShutdownManager.IsShutdownRequested && !View.CanClose) e.Cancel = true; };
        Closed += (_, _) => View.Dispose();
    }
}
