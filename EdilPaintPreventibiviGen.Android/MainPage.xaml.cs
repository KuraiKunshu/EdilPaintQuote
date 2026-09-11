using System.Collections.ObjectModel;
using EdilPaintPreventibiviGen.Android.Models;
using EdilPaintPreventibiviGen.Android.Services;

namespace EdilPaintPreventibiviGen.Android;

public partial class MainPage : ContentPage
{
    private enum HomeSection
    {
        Dashboard,
        Quotes,
        Customers,
        Orders,
        Catalog
    }

    private readonly CredentialStore _credentialStore = new();
    private readonly MobileDatabaseService _databaseService = new();
    private readonly ObservableCollection<QuoteSummary> _quotes = [];
    private readonly ObservableCollection<CustomerRecord> _customers = [];
    private readonly ObservableCollection<SupplierOrderSummary> _orders = [];
    private readonly ObservableCollection<CatalogItem> _catalogItems = [];
    private List<SupplierOrderSummary> _loadedOrders = [];
    private string _connectionString = string.Empty;
    private CancellationTokenSource? _quoteSearchCts;
    private CancellationTokenSource? _customerSearchCts;
    private CancellationTokenSource? _orderSearchCts;
    private CancellationTokenSource? _catalogSearchCts;
    private HomeSection _activeSection = HomeSection.Dashboard;
    private QuoteLineKind _catalogKind = QuoteLineKind.Material;
    private bool _isLoading;
    private bool _reloadPending;
    private QuoteListFilter _quoteFilter = new();

    public MainPage()
    {
        InitializeComponent();
        MobileDocumentService.CleanExpiredExports();
        QuoteList.ItemsSource = _quotes;
        CustomerList.ItemsSource = _customers;
        OrderList.ItemsSource = _orders;
        CatalogList.ItemsSource = _catalogItems;
        StatusPicker.ItemsSource = QuoteStatusOptions.All.ToList();
        StatusPicker.SelectedIndex = 0;
        OrderStatusPicker.ItemsSource = new[] { "TUTTI" }.Concat(SupplierOrderStatusOptions.All).ToList();
        OrderStatusPicker.SelectedIndex = 0;
        OrderSortPicker.ItemsSource = SupplierOrderSortService.Options.ToList();
        OrderSortPicker.SelectedIndex = 0;
        UpdateCatalogButtons();
        UpdateSectionButtons();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            _connectionString = await _credentialStore.GetConnectionStringAsync();
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                ShowSetup();
                return;
            }

            ShowContent();
            await LoadActiveSectionAsync();
        }
        catch (Exception exception)
        {
            ShowSetup();
            await DisplayAlertAsync(
                "Accesso salvato non disponibile",
                MobileDatabaseService.GetUserMessage(exception),
                "OK");
        }
    }

    private async void OnSaveCredentialsClicked(object? sender, EventArgs e)
    {
        string value = ConnectionStringEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            await DisplayAlertAsync("Neon", "Inserisci la connection string.", "OK");
            return;
        }

        try
        {
            SetBusy(true);
            await _databaseService.TestConnectionAsync(value);
            await _credentialStore.SaveConnectionStringAsync(value);
            _connectionString = value;
            ConnectionStringEntry.Text = string.Empty;
            SetBusy(false);
            ShowContent();
            await LoadActiveSectionAsync();
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(
                "Connessione non riuscita",
                MobileDatabaseService.GetUserMessage(exception),
                "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnForgetCredentialsClicked(object? sender, EventArgs e)
    {
        string? choice = await DisplayActionSheetAsync("Impostazioni", "Annulla", null, "Configura email", "Esci");
        if (choice == "Configura email") { await Navigation.PushAsync(new MobileSettingsPage(_connectionString)); return; }
        if (choice != "Esci") return;
        bool confirm = await DisplayAlertAsync(
            "Esci",
            "Vuoi cancellare le credenziali salvate su questo dispositivo?",
            "Cancella",
            "Annulla");
        if (!confirm)
            return;

        _quotes.Clear();
        _customers.Clear();
        _orders.Clear();
        _loadedOrders.Clear();
        _catalogItems.Clear();
        BindableLayout.SetItemsSource(DashboardRecentList, null);
        SecureStorage.Default.Remove("mobile_settings_v1");
        _connectionString = string.Empty;
        _credentialStore.ClearConnectionString();
        ShowSetup();
    }

    private async void OnQuotesSectionClicked(object? sender, EventArgs e)
    {
        _activeSection = HomeSection.Quotes;
        UpdateSectionButtons();
        await LoadQuotesAsync();
    }

    private async void OnDashboardSectionClicked(object? sender, EventArgs e)
    {
        _activeSection = HomeSection.Dashboard;
        UpdateSectionButtons();
        await LoadDashboardAsync();
    }

    private async void OnCustomersSectionClicked(object? sender, EventArgs e)
    {
        _activeSection = HomeSection.Customers;
        UpdateSectionButtons();
        await LoadCustomersAsync();
    }

    private async void OnOrdersSectionClicked(object? sender, EventArgs e)
    {
        _activeSection = HomeSection.Orders;
        UpdateSectionButtons();
        await LoadOrdersAsync();
    }

    private async void OnCatalogSectionClicked(object? sender, EventArgs e)
    {
        _activeSection = HomeSection.Catalog;
        UpdateSectionButtons();
        await LoadCatalogAsync();
    }

    private async void OnRefreshQuotesClicked(object? sender, EventArgs e) => await LoadQuotesAsync();
    private async void OnQuoteFiltersClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new QuoteFiltersPage(_quoteFilter, filter => _quoteFilter = filter));
    private async void OnQuoteSearchRequested(object? sender, EventArgs e) => await LoadQuotesAsync();
    private async void OnStatusChanged(object? sender, EventArgs e) => await LoadQuotesAsync();
    private async void OnCustomerSearchRequested(object? sender, EventArgs e) => await LoadCustomersAsync();
    private async void OnRefreshDashboardClicked(object? sender, EventArgs e) => await LoadDashboardAsync();
    private async void OnRefreshOrdersClicked(object? sender, EventArgs e) => await LoadOrdersAsync();
    private async void OnOrderSearchRequested(object? sender, EventArgs e) => await LoadOrdersAsync();
    private async void OnOrderFilterChanged(object? sender, EventArgs e) => await LoadOrdersAsync();
    private async void OnCatalogSearchRequested(object? sender, EventArgs e) => await LoadCatalogAsync();

    private void OnQuoteSearchTextChanged(object? sender, TextChangedEventArgs e) =>
        DebounceSearch(ref _quoteSearchCts, LoadQuotesAsync);

    private void OnCustomerSearchTextChanged(object? sender, TextChangedEventArgs e) =>
        DebounceSearch(ref _customerSearchCts, LoadCustomersAsync);

    private void OnOrderSearchTextChanged(object? sender, TextChangedEventArgs e) =>
        DebounceSearch(ref _orderSearchCts, LoadOrdersAsync);

    private void OnCatalogSearchTextChanged(object? sender, TextChangedEventArgs e) =>
        DebounceSearch(ref _catalogSearchCts, LoadCatalogAsync);

    private void OnOrderSortChanged(object? sender, EventArgs e) => ApplyOrderSort();

    private async void OnQuoteRefreshViewRefreshing(object? sender, EventArgs e)
    {
        await LoadQuotesAsync();
        QuoteRefresh.IsRefreshing = false;
    }

    private async void OnCustomerRefreshViewRefreshing(object? sender, EventArgs e)
    {
        await LoadCustomersAsync();
        CustomerRefresh.IsRefreshing = false;
    }

    private async void OnDashboardRefreshViewRefreshing(object? sender, EventArgs e)
    {
        await LoadDashboardAsync();
        DashboardPanel.IsRefreshing = false;
    }

    private async void OnOrderRefreshViewRefreshing(object? sender, EventArgs e)
    {
        await LoadOrdersAsync();
        OrderRefresh.IsRefreshing = false;
    }

    private async void OnCatalogRefreshViewRefreshing(object? sender, EventArgs e)
    {
        await LoadCatalogAsync();
        CatalogRefresh.IsRefreshing = false;
    }

    private async void OnQuoteSelected(object? sender, SelectionChangedEventArgs e)
    {
        var quote = e.CurrentSelection.FirstOrDefault() as QuoteSummary;
        QuoteList.SelectedItem = null;
        if (quote == null)
            return;

        try
        {
            SetBusy(true);
            QuoteDetail detail = await _databaseService.GetQuoteAsync(_connectionString, quote.QuoteNumber);
            await Navigation.PushAsync(new QuoteDetailPage(_connectionString, detail));
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(
                "Dettaglio non disponibile",
                MobileDatabaseService.GetUserMessage(exception),
                "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnCustomerSelected(object? sender, SelectionChangedEventArgs e)
    {
        var customer = e.CurrentSelection.FirstOrDefault() as CustomerRecord;
        CustomerList.SelectedItem = null;
        if (customer != null)
            await Navigation.PushAsync(new CustomerEditorPage(_connectionString, customer));
    }

    private async void OnOrderSelected(object? sender, SelectionChangedEventArgs e)
    {
        var order = e.CurrentSelection.FirstOrDefault() as SupplierOrderSummary;
        OrderList.SelectedItem = null;
        if (order != null)
            await Navigation.PushAsync(new SupplierOrderEditorPage(_connectionString, order));
    }

    private async void OnCatalogItemSelected(object? sender, SelectionChangedEventArgs e)
    {
        var item = e.CurrentSelection.FirstOrDefault() as CatalogItem;
        CatalogList.SelectedItem = null;
        if (item != null)
            await Navigation.PushAsync(new CatalogEditorPage(_connectionString, _catalogKind, item));
    }

    private async void OnAddCatalogItemClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new CatalogEditorPage(_connectionString, _catalogKind));

    private async void OnDashboardQuoteTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is QuoteSummary summary)
            await OpenQuoteAsync(summary);
    }

    private async void OnMaterialCatalogClicked(object? sender, EventArgs e)
    {
        _catalogItems.Clear();
        _catalogKind = QuoteLineKind.Material;
        UpdateCatalogButtons();
        await LoadCatalogAsync();
    }

    private async void OnLaborCatalogClicked(object? sender, EventArgs e)
    {
        _catalogItems.Clear();
        _catalogKind = QuoteLineKind.Labor;
        UpdateCatalogButtons();
        await LoadCatalogAsync();
    }

    private async void OnAddQuoteClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new QuoteEditorPage(_connectionString));

    private async void OnAddCustomerClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new CustomerEditorPage(_connectionString));

    private async Task LoadActiveSectionAsync()
    {
        switch (_activeSection)
        {
            case HomeSection.Dashboard:
                await LoadDashboardAsync();
                break;
            case HomeSection.Quotes:
                await LoadQuotesAsync();
                break;
            case HomeSection.Customers:
                await LoadCustomersAsync();
                break;
            case HomeSection.Orders:
                await LoadOrdersAsync();
                break;
            case HomeSection.Catalog:
                await LoadCatalogAsync();
                break;
        }
    }

    private async Task LoadDashboardAsync()
    {
        if (!CanLoad(HomeSection.Dashboard))
            return;

        try
        {
            SetBusy(true);
            DashboardSnapshot snapshot = await _databaseService.GetDashboardAsync(_connectionString);
            DashboardQuotesLabel.Text = snapshot.TotalQuotes.ToString();
            DashboardCustomersLabel.Text = snapshot.Customers.ToString();
            DashboardOpenLabel.Text = snapshot.SentOpenQuotes.ToString();
            DashboardOrdersLabel.Text = snapshot.PendingOrders.ToString();
            DashboardConfirmedCountLabel.Text = snapshot.ConfirmedQuotes == 1
                ? "1 preventivo confermato"
                : $"{snapshot.ConfirmedQuotes} preventivi confermati";
            DashboardValueLabel.Text = snapshot.ConfirmedValueDisplay;
            BindableLayout.SetItemsSource(DashboardRecentList, snapshot.RecentQuotes);
            SubtitleLabel.Text = $"{snapshot.TotalQuotes} preventivi, {snapshot.PendingOrders} ordini aperti";
        }
        catch (Exception exception)
        {
            await ShowLoadErrorAsync("Dashboard non disponibile", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadQuotesAsync()
    {
        if (!CanLoad(HomeSection.Quotes))
            return;

        try
        {
            SetBusy(true);
            string search = QuoteSearchBox.Text?.Trim() ?? string.Empty;
            var status = StatusPicker.SelectedItem as QuoteStatusOption;
            IReadOnlyList<QuoteSummary> quotes = await _databaseService.GetQuoteSummariesAsync(
                _connectionString,
                search,
                status?.Value);

            _quotes.Clear();
            foreach (var quote in _quoteFilter.Apply(quotes))
                _quotes.Add(quote);
            QuoteCountLabel.Text = $"{_quotes.Count} di {quotes.Count} preventivi";
            SubtitleLabel.Text = QuoteCountLabel.Text;
        }
        catch (Exception exception)
        {
            await ShowLoadErrorAsync("Preventivi non disponibili", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadCustomersAsync()
    {
        if (!CanLoad(HomeSection.Customers))
            return;

        try
        {
            SetBusy(true);
            string search = CustomerSearchBox.Text?.Trim() ?? string.Empty;
            IReadOnlyList<CustomerRecord> customers = await _databaseService.GetCustomersAsync(
                _connectionString,
                search);

            _customers.Clear();
            foreach (var customer in customers)
                _customers.Add(customer);
            CustomerCountLabel.Text = _customers.Count == 1 ? "1 cliente" : $"{_customers.Count} clienti";
            SubtitleLabel.Text = CustomerCountLabel.Text;
        }
        catch (Exception exception)
        {
            await ShowLoadErrorAsync("Clienti non disponibili", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadOrdersAsync()
    {
        if (!CanLoad(HomeSection.Orders))
            return;

        try
        {
            SetBusy(true);
            string status = OrderStatusPicker.SelectedItem as string ?? "TUTTI";
            IReadOnlyList<SupplierOrderSummary> orders = await _databaseService.GetSupplierOrdersAsync(
                _connectionString,
                OrderSearchBox.Text?.Trim() ?? string.Empty,
                status);
            _loadedOrders = orders.ToList();
            ApplyOrderSort();
            OrderCountLabel.Text = _orders.Count == 1 ? "1 ordine" : $"{_orders.Count} ordini";
            SubtitleLabel.Text = OrderCountLabel.Text;
        }
        catch (Exception exception)
        {
            await ShowLoadErrorAsync("Ordini non disponibili", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyOrderSort()
    {
        var option = OrderSortPicker.SelectedItem as SupplierOrderSortOption
                     ?? SupplierOrderSortService.Options[0];
        IReadOnlyList<SupplierOrderSummary> sorted = SupplierOrderSortService.Sort(_loadedOrders, option.Mode);
        _orders.Clear();
        foreach (SupplierOrderSummary order in sorted)
            _orders.Add(order);
        OrderCountLabel.Text = _orders.Count == 1 ? "1 ordine" : $"{_orders.Count} ordini";
    }

    private async Task LoadCatalogAsync()
    {
        if (!CanLoad(HomeSection.Catalog))
            return;

        try
        {
            SetBusy(true);
            QuoteLineKind requestedKind = _catalogKind;
            IReadOnlyList<CatalogItem> items = await _databaseService.GetCatalogAsync(
                _connectionString,
                requestedKind,
                CatalogSearchBox.Text?.Trim() ?? string.Empty);
            if (requestedKind != _catalogKind) return;
            _catalogItems.Clear();
            foreach (CatalogItem item in items)
                _catalogItems.Add(item);
            CatalogCountLabel.Text = _catalogItems.Count == 1 ? "1 voce" : $"{_catalogItems.Count} voci";
            SubtitleLabel.Text = _catalogKind == QuoteLineKind.Material
                ? $"{_catalogItems.Count} materiali"
                : $"{_catalogItems.Count} lavorazioni";
        }
        catch (Exception exception)
        {
            await ShowLoadErrorAsync("Catalogo non disponibile", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void DebounceSearch(ref CancellationTokenSource? source, Func<Task> loadAction)
    {
        source?.Cancel();
        source?.Dispose();
        source = new CancellationTokenSource();
        CancellationToken token = source.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, token);
                await MainThread.InvokeOnMainThreadAsync(loadAction);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void ShowSetup()
    {
        PageTitleLabel.Text = "Preventivi e clienti";
        SubtitleLabel.Text = "Connessione a Neon";
        SetupPanel.IsVisible = true;
        QuotesPanel.IsVisible = false;
        CustomersPanel.IsVisible = false;
        DashboardPanel.IsVisible = false;
        OrdersPanel.IsVisible = false;
        CatalogPanel.IsVisible = false;
        SectionBar.IsVisible = false;
        ForgetButton.IsVisible = false;
    }

    private void ShowContent()
    {
        SetupPanel.IsVisible = false;
        SectionBar.IsVisible = true;
        ForgetButton.IsVisible = true;
        UpdateSectionButtons();
    }

    private void UpdateSectionButtons()
    {
        bool hasConnection = !string.IsNullOrWhiteSpace(_connectionString);
        DashboardPanel.IsVisible = hasConnection && _activeSection == HomeSection.Dashboard;
        QuotesPanel.IsVisible = hasConnection && _activeSection == HomeSection.Quotes;
        CustomersPanel.IsVisible = hasConnection && _activeSection == HomeSection.Customers;
        OrdersPanel.IsVisible = hasConnection && _activeSection == HomeSection.Orders;
        CatalogPanel.IsVisible = hasConnection && _activeSection == HomeSection.Catalog;
        PageTitleLabel.Text = _activeSection switch
        {
            HomeSection.Dashboard => "Dashboard",
            HomeSection.Quotes => "Preventivi",
            HomeSection.Customers => "Clienti",
            HomeSection.Orders => "Ordini",
            _ => "Cataloghi"
        };

        StyleSectionButton(DashboardSectionButton, _activeSection == HomeSection.Dashboard);
        StyleSectionButton(QuotesSectionButton, _activeSection == HomeSection.Quotes);
        StyleSectionButton(CustomersSectionButton, _activeSection == HomeSection.Customers);
        StyleSectionButton(OrdersSectionButton, _activeSection == HomeSection.Orders);
        StyleSectionButton(CatalogSectionButton, _activeSection == HomeSection.Catalog);
    }

    private static void StyleSectionButton(Button button, bool selected)
    {
        button.BackgroundColor = Color.FromArgb(selected ? "#B3261E" : "#FFFFFF");
        button.TextColor = Color.FromArgb(selected ? "#FFFFFF" : "#B3261E");
    }

    private void UpdateCatalogButtons()
    {
        StyleSectionButton(MaterialCatalogButton, _catalogKind == QuoteLineKind.Material);
        StyleSectionButton(LaborCatalogButton, _catalogKind == QuoteLineKind.Labor);
    }

    private async Task OpenQuoteAsync(QuoteSummary quote)
    {
        try
        {
            SetBusy(true);
            QuoteDetail detail = await _databaseService.GetQuoteAsync(_connectionString, quote.QuoteNumber);
            await Navigation.PushAsync(new QuoteDetailPage(_connectionString, detail));
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Dettaglio non disponibile", MobileDatabaseService.GetUserMessage(exception), "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy)
    {
        _isLoading = isBusy;
        Busy.IsVisible = isBusy;
        Busy.IsRunning = isBusy;
        QuoteList.IsEnabled = CustomerList.IsEnabled = OrderList.IsEnabled = CatalogList.IsEnabled = !isBusy;
        ForgetButton.IsEnabled = !isBusy;
        if (!isBusy && _reloadPending)
        {
            _reloadPending = false;
            Dispatcher.Dispatch(async () => await LoadActiveSectionAsync());
        }
    }

    private bool CanLoad(HomeSection section)
    {
        if (string.IsNullOrWhiteSpace(_connectionString) || section != _activeSection) return false;
        if (!_isLoading) return true;
        _reloadPending = true;
        return false;
    }

    private Task ShowLoadErrorAsync(string title, Exception exception)
    {
        SubtitleLabel.Text = exception is DatabaseReadTimeoutException || exception is Npgsql.NpgsqlException and not Npgsql.PostgresException
            ? "Offline - dati non aggiornati" : "Dati non aggiornati";
        return DisplayAlertAsync(title, MobileDatabaseService.GetUserMessage(exception), "OK");
    }
}
