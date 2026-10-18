using System.Windows;
using System.Windows.Threading;
using EdilPaintPreventibiviGen.Services;
using EdilPaintPreventibiviGen.ViewModels;
namespace EdilPaintPreventibiviGen.Views;

public partial class HistoryWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private bool _closed;
    public HistoryView View { get; }
    public bool CanClose => IsEnabled && View.CanClose;

    public static HistoryWindow? FindBusy() => Application.Current.Windows.OfType<HistoryWindow>().FirstOrDefault(window => !window.CanClose);

    public static void Open(MainViewModel vm, string? quoteNumber = null)
    {
        var window = Application.Current.Windows.OfType<HistoryWindow>().FirstOrDefault(item => ReferenceEquals(item._vm, vm));
        if (window == null) { window = new HistoryWindow(vm, quoteNumber); window.Show(); }
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        if (!string.IsNullOrWhiteSpace(quoteNumber))
            _ = window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
            {
                if (window.IsVisible && window.View.CanClose) await window.View.FocusQuoteAsync(quoteNumber);
            }));
    }

    public HistoryWindow(MainViewModel vm, string? initialSearch = null)
    {
        InitializeComponent();
        _vm = vm;
        View = new HistoryView(vm, initialSearch);
        Content = View;
        View.CloseRequested += (_, _) => Close();
        View.OpenQuoteRequested += (_, _) => { Application.Current.MainWindow?.Activate(); Close(); };
        _refreshTimer.Tick += OnRefresh;
        Loaded += (_, e) => { if (ReferenceEquals(e.OriginalSource, this)) _refreshTimer.Start(); };
        QuoteHistoryService.Changed += OnHistoryChanged;
        Closing += (_, e) => { if (!AppShutdownManager.IsShutdownRequested && !CanClose) e.Cancel = true; };
        Closed += (_, _) =>
        {
            _closed = true;
            _refreshTimer.Stop();
            _refreshTimer.Tick -= OnRefresh;
            QuoteHistoryService.Changed -= OnHistoryChanged;
            View.Dispose();
        };
    }
    private async void OnRefresh(object? sender, EventArgs e)
    {
        if (!_closed && IsVisible) await View.RefreshCurrentAsync();
    }
    private void OnHistoryChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(async () =>
    {
        if (!_closed && IsVisible) await View.RefreshCurrentAsync();
    }));
}
