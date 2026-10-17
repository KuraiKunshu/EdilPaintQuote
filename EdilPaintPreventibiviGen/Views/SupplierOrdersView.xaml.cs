using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using EdilPaintPreventibiviGen.ViewModels;

namespace EdilPaintPreventibiviGen.Views;

public partial class SupplierOrdersView : UserControl, IDisposable
{
    private Window HostWindow => Window.GetWindow(this) ?? throw new InvalidOperationException("La sezione non e' collegata a una finestra.");
    public event EventHandler? CloseRequested;
    private void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private readonly MainViewModel _vm;
    private readonly QuoteHistoryService _historyService;
    private readonly List<QuoteHistorySummary> _quotes = new();
    private readonly List<QuoteHistorySummary> _loadedQuotes = new();
    private readonly ListCollectionView _orderView;
    private CancellationTokenSource? _refreshCts;
    private bool _isRefreshing;
    private bool _isSaving;
    private bool _hasLoaded;
    private bool _detailsOpen;
    private bool _closed;
    public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register(nameof(CardWidth), typeof(double), typeof(SupplierOrdersView), new PropertyMetadata(240d));
    public double CardWidth { get => (double)GetValue(CardWidthProperty); set => SetValue(CardWidthProperty, value); }
    public bool IsEmbedded { get; set; }
    public bool CanClose => !_isSaving && !_detailsOpen;
    public string SelectedNumber { get; private set; } = "";
    public event EventHandler<WorkContextEventArgs>? ContextSelected;
    public event EventHandler? Refreshed;
    public QuoteHistorySummary? FindOrder(string number) => _loadedQuotes.FirstOrDefault(order => order.QuoteNumber == number);
    public Task RefreshCurrentAsync() => _detailsOpen || _isSaving || !_hasLoaded || TxtSearch.IsKeyboardFocusWithin ||
        CmbStatusFilter.IsDropDownOpen || CmbSortOrder.IsDropDownOpen ? Task.CompletedTask : RefreshAsync(TxtSearch.Text?.Trim() ?? "");
    public void SelectQuote(string number) => SelectedNumber = number;

    public IReadOnlyList<string> MaterialStatusOptions { get; } =
    [
        "Da ordinare",
        "Ordinato",
        "Da ritirare",
        "In magazzino",
        "Consegnato",
        "Non disponibile"
    ];

    public IReadOnlyList<string> MaterialStatusFilterOptions { get; } =
    [
        "Tutti gli stati",
        "Senza stato",
        "Da ordinare",
        "Ordinato",
        "Da ritirare",
        "In magazzino",
        "Consegnato",
        "Non disponibile"
    ];

    public SupplierOrdersView(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _historyService = new QuoteHistoryService(App.DataService, StoragePathService.Instance);
        _orderView = new ListCollectionView(_quotes);
        ItemsOrders.ItemsSource = _orderView;
        CmbStatusFilter.ItemsSource = MaterialStatusFilterOptions;
        CmbStatusFilter.SelectedIndex = 0;
        CmbSortOrder.ItemsSource = SupplierOrderSortService.Options;
        CmbSortOrder.SelectedIndex = 0;
        SizeChanged += (_, _) =>
        {
            double available = Math.Max(200, ActualWidth - 70);
            int columns = Math.Max(1, (int)(available / 252));
            CardWidth = available / columns - 12;
        };
        Loaded += async (_, _) =>
        {
            if (_hasLoaded) return;
            _hasLoaded = true;
            await RefreshAsync(TxtSearch.Text?.Trim() ?? string.Empty);
        };
    }
    public void Dispose()
    {
        _closed = true;
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = null;
    }

    private async Task RefreshAsync(string searchText = "")
    {
        if (_isRefreshing || _closed)
            return;

        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = AppShutdownManager.CreateLinkedTokenSource();
        var token = _refreshCts.Token;
        double scrollOffset = OrdersScroll.VerticalOffset;

        try
        {
            _isRefreshing = true;
            Cursor = Cursors.Wait;
            TxtSubtitle.Text = "Caricamento...";
            TxtFooterStatus.Text = "Aggiornamento in corso...";
            EmptyPanel.Visibility = Visibility.Collapsed;
            BtnSearch.IsEnabled = false;
            BtnRefresh.IsEnabled = false;
            CmbStatusFilter.IsEnabled = false;
            CmbSortOrder.IsEnabled = false;
            ChkGroupBySupplier.IsEnabled = false;
            ItemsOrders.IsEnabled = false;

            var summaries = await _historyService.LoadSupplierOrderSummariesAsync(
                searchText,
                int.MaxValue,
                token);

            token.ThrowIfCancellationRequested();

            if (!string.Equals(System.Text.Json.JsonSerializer.Serialize(_loadedQuotes), System.Text.Json.JsonSerializer.Serialize(summaries), StringComparison.Ordinal))
            {
                _loadedQuotes.Clear();
                _loadedQuotes.AddRange(summaries);
                ApplyOrderView();
            }
            else if (_loadedQuotes.Count == 0) ApplyOrderView();
            _ = Dispatcher.BeginInvoke(new Action(() => { if (!_closed) OrdersScroll.ScrollToVerticalOffset(scrollOffset); }));
            TxtFooterStatus.Text = $"Aggiornato alle {DateTime.Now:HH:mm}";
            Refreshed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            TxtSubtitle.Text = "Caricamento non riuscito.";
            TxtFooterStatus.Text = "Errore durante il caricamento";
            MessageBox.Show(
                $"Errore durante il caricamento degli ordini.\n\n{ex.Message}",
                "Ordini",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            Cursor = null;
            _isRefreshing = false;
            BtnSearch.IsEnabled = true;
            BtnRefresh.IsEnabled = true;
            CmbStatusFilter.IsEnabled = true;
            CmbSortOrder.IsEnabled = true;
            ChkGroupBySupplier.IsEnabled = true;
            ItemsOrders.IsEnabled = true;
        }
    }

    private void ApplyOrderView()
    {
        if (_orderView == null) return;
        string selectedStatus = CmbStatusFilter.SelectedItem as string ?? "Tutti gli stati";
        IEnumerable<QuoteHistorySummary> filtered = _loadedQuotes;

        if (string.Equals(selectedStatus, "Senza stato", StringComparison.Ordinal))
        {
            filtered = filtered.Where(summary => string.IsNullOrWhiteSpace(summary.MaterialStatus));
        }
        else if (!string.Equals(selectedStatus, "Tutti gli stati", StringComparison.Ordinal))
        {
            filtered = filtered.Where(summary => string.Equals(
                summary.MaterialStatus?.Trim(),
                selectedStatus,
                StringComparison.OrdinalIgnoreCase));
        }

        SupplierOrderSortMode sortMode = CmbSortOrder.SelectedItem is SupplierOrderSortOption sortOption
            ? sortOption.Mode
            : SupplierOrderSortMode.OrderDateDescending;

        bool grouped = ChkGroupBySupplier.IsChecked == true;
        using (_orderView.DeferRefresh())
        {
            _orderView.GroupDescriptions.Clear();
            if (grouped)
                _orderView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(QuoteHistorySummary.OrderSupplierGroup))
                    { StringComparison = StringComparison.OrdinalIgnoreCase });
            _quotes.Clear();
            foreach (QuoteHistorySummary summary in grouped
                         ? SupplierOrderSortService.SortBySupplier(filtered, sortMode)
                         : SupplierOrderSortService.Sort(filtered, sortMode))
                _quotes.Add(summary);
        }
        // The plain list avoids per-row notifications while grouping is deferred.
        _orderView.Refresh();
        TxtSortOrderLabel.Text = grouped ? "Ordina nel fornitore" : "Ordina per";

        string visibleLabel = _quotes.Count == 1 ? "1 ordine" : $"{_quotes.Count} ordini";
        TxtSubtitle.Text = _quotes.Count == _loadedQuotes.Count
            ? visibleLabel
            : $"{visibleLabel} su {_loadedQuotes.Count}";
        TxtVisibleCount.Text = grouped ? $"{visibleLabel} · {_orderView.Groups?.Count ?? 0} gruppi" : visibleLabel;

        bool hasVisibleOrders = _quotes.Count > 0;
        EmptyPanel.Visibility = hasVisibleOrders ? Visibility.Collapsed : Visibility.Visible;
        TxtEmptyDetail.Text = _loadedQuotes.Count == 0
            ? "Non ci sono ordini registrati."
            : "Nessun ordine corrisponde ai criteri selezionati.";
    }

    private async Task<bool> SaveSupplierAsync(QuoteHistorySummary summary, QuoteHistorySummary original)
    {
        if (_isSaving)
            return false;

        try
        {
            _isSaving = true;
            Cursor = Cursors.Wait;

            string deviceName = DeviceNameService.GetCurrentDeviceName();
            if (summary.MaterialsOrderedByCustomer)
                SupplierOrderAssignmentService.ApplyCustomerOrderChoice(summary, orderedByCustomer: true);
            await _historyService.UpdateSupplierInfoAsync(summary.QuoteNumber, new QuoteSupplierInfo
            {
                SupplierName = summary.SupplierName,
                MaterialsOrderedByCustomer = summary.MaterialsOrderedByCustomer,
                MaterialOrderDate = summary.MaterialOrderDate,
                ExpectedDeliveryDate = summary.ExpectedDeliveryDate,
                MaterialStatus = summary.MaterialStatus,
                DeviceName = deviceName
            });

            summary.LastModifiedByDevice = deviceName;
            original.SupplierName = summary.SupplierName;
            original.MaterialsOrderedByCustomer = summary.MaterialsOrderedByCustomer;
            original.MaterialOrderDate = summary.MaterialOrderDate;
            original.ExpectedDeliveryDate = summary.ExpectedDeliveryDate;
            original.MaterialStatus = summary.MaterialStatus;
            original.LastModifiedByDevice = deviceName;
            TxtFooterStatus.Text = $"Ordine del preventivo {summary.QuoteNumber} salvato";
            ApplyOrderView();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Suppliers] Errore salvataggio {summary.QuoteNumber}: {ex.Message}");
            MessageBox.Show(
                $"Errore durante il salvataggio del preventivo {summary.QuoteNumber}.\n\n{ex.Message}",
                "Ordini",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            Cursor = null;
            _isSaving = false;
        }
    }

    private async Task PrepareOrderMailAsync(QuoteHistorySummary summary, Window owner, QuoteHistorySummary original)
    {
        try
        {
            if (summary.MaterialsOrderedByCustomer)
                SupplierOrderAssignmentService.ApplyCustomerOrderChoice(summary, orderedByCustomer: true);

            var fullEntry = await _historyService.GetQuoteByNumberAsync(summary.QuoteNumber);
            if (fullEntry == null)
            {
                MessageBox.Show(
                    "Preventivo non trovato nello storico.",
                    "Ordini",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            fullEntry.SupplierName = summary.SupplierName;
            fullEntry.MaterialsOrderedByCustomer = summary.MaterialsOrderedByCustomer;
            fullEntry.MaterialOrderDate = summary.MaterialOrderDate;
            fullEntry.ExpectedDeliveryDate = summary.ExpectedDeliveryDate;
            fullEntry.MaterialStatus = summary.MaterialStatus;

            if (string.IsNullOrWhiteSpace(fullEntry.SupplierName))
            {
                MessageBox.Show(
                    "Seleziona prima un fornitore.",
                    "Ordini",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var draft = SupplierOrderMailService.CreateDraft(fullEntry, _vm.AllCustomers);
            if (string.IsNullOrWhiteSpace(draft.Recipient))
            {
                MessageBox.Show(
                    "Il fornitore selezionato non ha un indirizzo email in anagrafica. La finestra verra' preparata senza destinatario.",
                    "Ordini",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            var win = new SupplierOrderMailWindow(fullEntry, draft) { Owner = owner };
            if (win.ShowDialog() == true && win.WasRegisteredAsSent)
            {
                summary.MaterialOrderDate ??= win.RegisteredAtUtc.ToLocalTime().Date;
                if (string.IsNullOrWhiteSpace(summary.MaterialStatus))
                    summary.MaterialStatus = "Ordinato";

                await SaveSupplierAsync(summary, original);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Suppliers] Errore mail ordine {summary.QuoteNumber}: {ex.Message}");
            MessageBox.Show(
                $"Errore durante la gestione dell'ordine.\n\n{ex.Message}",
                "Ordini",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void OnOpenOrderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: QuoteHistorySummary summary }) return;
        SelectedNumber = summary.QuoteNumber;
        ContextSelected?.Invoke(this, new(summary.QuoteNumber, summary.OrderSiteTitle));
        if (!IsEmbedded) await OpenOrderAsync(summary.QuoteNumber);
    }

    public async Task OpenOrderAsync(string number)
    {
        if (_detailsOpen || _isSaving) return;
        var summary = FindOrder(number);
        if (summary == null) throw new InvalidOperationException("L’ordine non è presente nell’elenco aggiornato. Aprilo nello storico per verificarne lo stato.");
        var details = new SupplierOrderDetailsWindow(_vm, summary, MaterialStatusOptions,
            draft => SaveSupplierAsync(draft, summary),
            (draft, owner) => PrepareOrderMailAsync(draft, owner, summary)) { Owner = HostWindow };
        _detailsOpen = true;
        try { details.ShowDialog(); }
        finally { _detailsOpen = false; }
        // History can change the quote status while the order detail is open.
        await RefreshAsync(TxtSearch.Text?.Trim() ?? string.Empty);
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e)
    {
        await RefreshAsync(TxtSearch.Text?.Trim() ?? string.Empty);
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await RefreshAsync(TxtSearch.Text?.Trim() ?? string.Empty);
    }

    private void OnStatusFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isRefreshing)
            ApplyOrderView();
    }

    private void OnSortOrderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isRefreshing)
            ApplyOrderView();
    }

    private void OnSupplierGroupingChanged(object sender, RoutedEventArgs e)
    {
        if (!_isRefreshing) ApplyOrderView();
    }

    private async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        await RefreshAsync(TxtSearch.Text?.Trim() ?? string.Empty);
    }

}
