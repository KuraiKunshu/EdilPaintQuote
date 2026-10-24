using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using EdilPaintPreventibiviGen.ViewModels;

namespace EdilPaintPreventibiviGen.Views;

public partial class WorkManagementWindow : Window
{
    private readonly WorkScheduleView _calendar;
    private readonly SupplierOrdersView _orders;
    private readonly HistoryView _history;
    private readonly WorkContextSelection _selection = new();
    private readonly Dictionary<UserControl, Window> _detached = [];
    private readonly DependencyPropertyDescriptor _busyDescriptor = DependencyPropertyDescriptor.FromProperty(QuoteActionsControl.IsBusyProperty, typeof(QuoteActionsControl));
    private readonly DependencyPropertyDescriptor _menuDescriptor = DependencyPropertyDescriptor.FromProperty(QuoteActionsControl.IsMenuOpenProperty, typeof(QuoteActionsControl));
    private readonly DispatcherTimer _changeTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _closed, _actionBusy, _refreshing, _updatingSelection, _changingSection;
    private double _sideWidth = 600;
    public bool CanClose => IsEnabled && !_actionBusy && !CommonActions.IsBusy && !CommonActions.IsMenuOpen &&
        _calendar.CanClose && _orders.CanClose && _history.CanClose && _detached.Values.All(window => window.IsEnabled);

    public WorkManagementWindow(MainViewModel vm)
    {
        InitializeComponent();
        _calendar = new WorkScheduleView();
        _calendar.ConfigureEmbedded();
        _orders = new SupplierOrdersView(vm) { IsEmbedded = true };
        _history = new HistoryView(vm);
        _history.ConfigureEmbedded();
        CalendarHost.Content = _calendar;
        OrdersHost.Content = _orders;
        HistoryHost.Content = _history;
        _calendar.ContextSelected += OnContextSelected;
        _orders.ContextSelected += OnContextSelected;
        _orders.Refreshed += (_, _) => UpdateContext();
        _history.ContextSelected += OnContextSelected;
        _calendar.SnapshotChanged += OnSnapshotChanged;
        _history.OpenQuoteRequested += (_, _) => Application.Current.MainWindow?.Activate();
        _history.CloseRequested += (_, _) => ShowSection(false);
        _calendar.CloseRequested += (_, _) => Close();
        _busyDescriptor.AddValueChanged(CommonActions, OnCommonBusyChanged);
        _menuDescriptor.AddValueChanged(CommonActions, OnCommonBusyChanged);
        QuoteHistoryService.Changed += OnDataChanged;
        WorkScheduleActions.WorkCompleted += OnWorkCompleted;
        CommonActions.WorkCompleted += OnWorkCompleted;
        _changeTimer.Tick += OnChangeTimer;
        Closing += OnClosing;
        Closed += OnClosed;
        UpdateContext();
        ApplySplit();
    }

    public void ShowCalendar()
    {
        _changingSection = true;
        try { BtnCalendarTab.IsChecked = true; }
        finally { _changingSection = false; }
        UpdateSection();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void ShowSection(bool history, bool followContext = true)
    {
        _changingSection = true;
        try
        {
            BtnCalendarTab.IsChecked = false;
            BtnHistoryTab.IsChecked = history;
            BtnOrdersTab.IsChecked = !history;
            if (history) ChkSideBySide.IsChecked = true;
        }
        finally { _changingSection = false; }
        UpdateSection();
        if (history && followContext) _ = FollowSelectedQuoteAsync();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public async Task ShowQuoteAsync(string number)
    {
        ShowSection(true, followContext: false);
        await _history.FocusQuoteAsync(number);
        if (_detached.TryGetValue(_history, out var window)) { window.Show(); window.Activate(); }
    }

    private async void OnContextSelected(object? sender, WorkContextEventArgs e)
    {
        if (_closed || _updatingSelection || CommonActions.IsBusy || CommonActions.IsMenuOpen || _actionBusy ||
            !_calendar.CanClose || !_orders.CanClose || !_history.CanClose) return;
        _updatingSelection = true;
        try
        {
            _selection.Select(e.QuoteNumber, e.Title, e.Entry);
            _calendar.SelectQuote(_selection.QuoteNumber);
            _orders.SelectQuote(_selection.QuoteNumber);
            UpdateContext();
        }
        finally { _updatingSelection = false; }
        if (sender != _history && _history.IsVisible && !string.IsNullOrEmpty(e.QuoteNumber))
            await _history.FocusQuoteAsync(e.QuoteNumber);
    }

    private void UpdateContext()
    {
        if (_closed) return;
        TxtSelectedSite.Text = _selection.Title;
        TxtSelectedSite.ToolTip = _selection.Title;
        var entry = _selection.Entry;
        string crew = entry == null ? "" : string.Join(", ", entry.Employees.Select(employee =>
            string.IsNullOrWhiteSpace(employee.Abbreviation) ? $"{employee.FirstName} {employee.LastName}".Trim() : employee.Abbreviation));
        TxtSelectedInfo.Text = entry != null ? $"{entry.Date:dd/MM/yyyy} · {entry.TimeDisplay} · {(crew.Length == 0 ? "Squadra da assegnare" : crew)}" :
            _selection.QuoteNumber.Length > 0 ? $"Ordine {_selection.QuoteNumber}" :
            "Scegli un riquadro nel calendario o negli ordini per vedere qui i comandi del lavoro.";
        TxtSelectionLabel.Text = entry != null ? "INTERVENTO SELEZIONATO" : "ORDINE SELEZIONATO";
        TxtSelectionLabel.Visibility = entry != null || _selection.QuoteNumber.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ContextToolbar.Visibility = entry != null || _selection.QuoteNumber.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CommonActions.QuoteNumber = _selection.QuoteNumber;
        CommonActions.ScheduleEntry = entry;
        CommonActions.CanComplete = _calendar.IsCurrent;
        bool busy = _actionBusy || CommonActions.IsBusy || CommonActions.IsMenuOpen;
        BtnCalendarTab.IsEnabled = BtnOrdersTab.IsEnabled = BtnHistoryTab.IsEnabled = ChkSideBySide.IsEnabled = !busy;
        _calendar.IsEnabled = _orders.IsEnabled = _history.IsEnabled = !busy;
        bool quote = _selection.QuoteNumber.Length > 0;
        CommonActions.Visibility = BtnPlan.Visibility = BtnMaterials.Visibility = BtnCosts.Visibility = quote ? Visibility.Visible : Visibility.Collapsed;
        BtnPlan.IsEnabled = !busy && quote && _calendar.IsCurrent && _calendar.HasOrder(_selection.QuoteNumber);
        BtnEditEntry.IsEnabled = !busy && entry != null;
        BtnMaterials.IsEnabled = !busy && quote && _orders.FindOrder(_selection.QuoteNumber) != null;
        BtnCosts.IsEnabled = BtnShowQuote.IsEnabled = !busy && quote;
        BtnEditEntry.Visibility = entry == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnCommonBusyChanged(object? sender, EventArgs e)
    {
        UpdateContext();
        if (!CommonActions.IsBusy && !CommonActions.IsMenuOpen) OnDataChanged(sender, e);
    }
    private void OnWorkCompleted(object? sender, WorkScheduleCompletedEventArgs e)
    {
        if (_selection.Entry?.Id == e.Entry.Id)
        {
            _selection.Select(e.Entry.QuoteNumber, e.Entry.Title, e.Entry);
            UpdateContext();
        }
        OnDataChanged(sender, e);
    }
    private async void OnSnapshotChanged(object? sender, EventArgs e)
    {
        if (_closed || CommonActions.IsBusy || CommonActions.IsMenuOpen || _actionBusy) return;
        _selection.UpdateEntries(_calendar.Entries);
        _calendar.SelectQuote(_selection.QuoteNumber);
        _orders.SelectQuote(_selection.QuoteNumber);
        UpdateContext();
        await RefreshSideViewsAsync();
    }
    private async Task RefreshSideViewsAsync()
    {
        if (_refreshing || _closed || _actionBusy || CommonActions.IsBusy || CommonActions.IsMenuOpen) return;
        _refreshing = true;
        try
        {
            if (_orders.IsVisible) await _orders.RefreshCurrentAsync();
            if (_history.IsVisible) await _history.RefreshCurrentAsync();
            UpdateContext();
        }
        finally { _refreshing = false; }
    }
    private void OnDataChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (!_closed) { _changeTimer.Stop(); _changeTimer.Start(); }
    }));
    private async void OnChangeTimer(object? sender, EventArgs e)
    {
        _changeTimer.Stop();
        if (!_closed && !CommonActions.IsBusy && !CommonActions.IsMenuOpen) await _calendar.RefreshAsync();
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_actionBusy || _closed || CommonActions.IsBusy || CommonActions.IsMenuOpen) return;
        _actionBusy = true; UpdateContext();
        try { await action(); }
        catch (OperationCanceledException) when (_closed || AppShutdownManager.IsShutdownRequested) { }
        catch (Exception ex) { if (!_closed) MessageBox.Show(this, ex.Message, "Gestione lavori", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { _actionBusy = false; UpdateContext(); if (!_closed) await _calendar.RefreshAsync(); }
    }

    private async void OnSectionChanged(object sender, RoutedEventArgs e)
    {
        if (_history == null || _changingSection) return;
        UpdateSection();
        if (BtnHistoryTab.IsChecked == true) await FollowSelectedQuoteAsync();
        else await RefreshSideViewsAsync();
    }
    private Task FollowSelectedQuoteAsync() => !_closed && _selection.QuoteNumber.Length > 0 && _history.SelectedNumber != _selection.QuoteNumber
        ? _history.FocusQuoteAsync(_selection.QuoteNumber) : Task.CompletedTask;
    private void UpdateSection()
    {
        bool history = BtnHistoryTab.IsChecked == true;
        OrdersHost.Visibility = history ? Visibility.Collapsed : Visibility.Visible;
        HistoryHost.Visibility = history ? Visibility.Visible : Visibility.Collapsed;
        BtnDetached.Visibility = _detached.ContainsKey(history ? _history : _orders) ? Visibility.Visible : Visibility.Collapsed;
        ApplySplit();
    }
    private async void OnPlanClick(object sender, RoutedEventArgs e) => await RunAsync(() => _calendar.PlanSelectedAsync(_selection.QuoteNumber));
    private async void OnEditEntryClick(object sender, RoutedEventArgs e) { if (_selection.Entry is { } entry) await RunAsync(() => _calendar.EditEntryAsync(entry)); }
    private async void OnMaterialsClick(object sender, RoutedEventArgs e) => await RunAsync(() => _orders.OpenOrderAsync(_selection.QuoteNumber));
    private async void OnCostsClick(object sender, RoutedEventArgs e) => await RunAsync(() => _history.OpenCostsAsync(_selection.QuoteNumber));
    private async void OnShowQuoteClick(object sender, RoutedEventArgs e) => await ShowQuoteAsync(_selection.QuoteNumber);
    private async void OnRefreshClick(object sender, RoutedEventArgs e) { await _calendar.RefreshAsync(); await RefreshSideViewsAsync(); }
    private void OnLayoutChanged(object sender, RoutedEventArgs e)
    {
        if (_calendar == null || _changingSection) return;
        ApplySplit();
    }
    private void ApplySplit()
    {
        bool detachedCalendar = _detached.ContainsKey(_calendar);
        bool calendar = (BtnCalendarTab.IsChecked == true || ChkSideBySide.IsChecked == true) && !detachedCalendar;
        bool side = BtnCalendarTab.IsChecked != true || ChkSideBySide.IsChecked == true || detachedCalendar;
        bool split = calendar && side;
        CalendarHost.Visibility = calendar ? Visibility.Visible : Visibility.Collapsed;
        SidePane.Visibility = side ? Visibility.Visible : Visibility.Collapsed;
        CalendarColumn.MinWidth = calendar ? 360 : 0;
        CalendarColumn.Width = calendar ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SideColumn.MinWidth = side ? 380 : 0;
        double available = WorkspaceRoot.ActualWidth > 0 ? WorkspaceRoot.ActualWidth - 28 : Width - 28;
        double sideWidth = Math.Clamp(_sideWidth, 380, Math.Max(380, available * 0.46));
        SideColumn.Width = !side ? new GridLength(0) : split ? new GridLength(sideWidth) : new GridLength(1, GridUnitType.Star);
        DividerColumn.Width = new GridLength(split ? 8 : 0);
        WorkspaceSplitter.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        BtnDetachCalendar.Header = detachedCalendar ? "Mostra calendario separato" : "Separa calendario";
    }
    private void OnWorkspaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_calendar != null && e.WidthChanged) ApplySplit();
    }
    private void OnWindowMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button) { menu.PlacementTarget = button; menu.IsOpen = true; }
    }
    private void OnSplitResized(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) => _sideWidth = SideColumn.ActualWidth;
    private void OnDetachCalendarClick(object sender, RoutedEventArgs e) => Detach(_calendar, CalendarHost, "Calendario lavori");
    private void OnDetachSectionClick(object sender, RoutedEventArgs e)
    {
        if (BtnHistoryTab.IsChecked == true) Detach(_history, HistoryHost, "Storico preventivi");
        else Detach(_orders, OrdersHost, "Ordini");
    }
    private void Detach(UserControl view, ContentControl host, string title)
    {
        if (!CanClose) return;
        if (_detached.TryGetValue(view, out var existing)) { existing.Activate(); return; }
        host.Content = null;
        // Zoom belongs to the temporary window's root, never to the reusable view itself.
        var floatingRoot = new Grid();
        floatingRoot.Children.Add(view);
        Window window = view == _calendar ? new DetachedCalendarWindow() : view == _history ? new DetachedHistoryWindow() : new DetachedOrdersWindow();
        window.Title = title + " · chiudi per riagganciare";
        window.Content = floatingRoot;
        window.Owner = this;
        window.Width = view == _calendar ? 1260 : 1140;
        window.Height = 820; window.MinWidth = 500; window.MinHeight = 560;
        window.Icon = Icon; window.Background = Background;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _detached.Add(view, window);
        if (view == _calendar) _calendar.UseSharedCommands(false);
        if (view == _orders) _orders.IsEmbedded = false;
        window.Closing += (_, e) => { if (!_closed && !AppShutdownManager.IsShutdownRequested && !CanClose) e.Cancel = true; };
        window.Closed += (_, _) =>
        {
            floatingRoot.Children.Remove(view);
            window.Content = null;
            _detached.Remove(view);
            if (view == _calendar) _calendar.UseSharedCommands(true);
            if (view == _orders) _orders.IsEmbedded = true;
            if (!_closed) { host.Content = view; UpdateSection(); ApplySplit(); }
        };
        window.Show(); UpdateSection(); ApplySplit();
    }
    private void OnReattachClick(object sender, RoutedEventArgs e)
    {
        if (_detached.TryGetValue(BtnHistoryTab.IsChecked == true ? _history : _orders, out var window)) window.Close();
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!AppShutdownManager.IsShutdownRequested && !CanClose) e.Cancel = true; }
    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _changeTimer.Stop();
        _changeTimer.Tick -= OnChangeTimer;
        QuoteHistoryService.Changed -= OnDataChanged;
        WorkScheduleActions.WorkCompleted -= OnWorkCompleted;
        CommonActions.WorkCompleted -= OnWorkCompleted;
        _busyDescriptor.RemoveValueChanged(CommonActions, OnCommonBusyChanged);
        _menuDescriptor.RemoveValueChanged(CommonActions, OnCommonBusyChanged);
        _calendar.Dispose(); _orders.Dispose(); _history.Dispose();
    }
    // Distinct types give each detached section its own remembered window dimensions.
    private sealed class DetachedCalendarWindow : Window { }
    private sealed class DetachedOrdersWindow : Window { }
    private sealed class DetachedHistoryWindow : Window { }
}
