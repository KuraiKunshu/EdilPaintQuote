using System.Collections.ObjectModel;
using EdilPaintPreventibiviGen.Android.Models;
using EdilPaintPreventibiviGen.Android.Services;

namespace EdilPaintPreventibiviGen.Android;

public partial class MainPage : ContentPage
{
    private enum HomeSection
    {
        Quotes,
        Customers
    }

    private readonly CredentialStore _credentialStore = new();
    private readonly MobileDatabaseService _databaseService = new();
    private readonly ObservableCollection<QuoteSummary> _quotes = [];
    private readonly ObservableCollection<CustomerRecord> _customers = [];
    private string _connectionString = string.Empty;
    private CancellationTokenSource? _quoteSearchCts;
    private CancellationTokenSource? _customerSearchCts;
    private HomeSection _activeSection = HomeSection.Quotes;
    private bool _isLoading;

    public MainPage()
    {
        InitializeComponent();
        QuoteList.ItemsSource = _quotes;
        CustomerList.ItemsSource = _customers;
        StatusPicker.ItemsSource = QuoteStatusOptions.All.ToList();
        StatusPicker.SelectedIndex = 0;
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
            _connectionString = string.Empty;
            _credentialStore.ClearConnectionString();
            ShowSetup();
            await DisplayAlertAsync(
                "Accesso salvato non disponibile",
                $"Le credenziali salvate sono state cancellate da questo dispositivo.\n\n{MobileDatabaseService.GetUserMessage(exception)}",
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
        bool confirm = await DisplayAlertAsync(
            "Esci",
            "Vuoi cancellare le credenziali salvate su questo dispositivo?",
            "Cancella",
            "Annulla");
        if (!confirm)
            return;

        _quotes.Clear();
        _customers.Clear();
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

    private async void OnCustomersSectionClicked(object? sender, EventArgs e)
    {
        _activeSection = HomeSection.Customers;
        UpdateSectionButtons();
        await LoadCustomersAsync();
    }

    private async void OnRefreshQuotesClicked(object? sender, EventArgs e) => await LoadQuotesAsync();
    private async void OnQuoteSearchRequested(object? sender, EventArgs e) => await LoadQuotesAsync();
    private async void OnStatusChanged(object? sender, EventArgs e) => await LoadQuotesAsync();
    private async void OnCustomerSearchRequested(object? sender, EventArgs e) => await LoadCustomersAsync();

    private void OnQuoteSearchTextChanged(object? sender, TextChangedEventArgs e) =>
        DebounceSearch(ref _quoteSearchCts, LoadQuotesAsync);

    private void OnCustomerSearchTextChanged(object? sender, TextChangedEventArgs e) =>
        DebounceSearch(ref _customerSearchCts, LoadCustomersAsync);

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

    private async void OnAddQuoteClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new QuoteEditorPage(_connectionString));

    private async void OnAddCustomerClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new CustomerEditorPage(_connectionString));

    private async Task LoadActiveSectionAsync()
    {
        if (_activeSection == HomeSection.Quotes)
            await LoadQuotesAsync();
        else
            await LoadCustomersAsync();
    }

    private async Task LoadQuotesAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString) || _isLoading)
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
            foreach (var quote in quotes)
                _quotes.Add(quote);
            QuoteCountLabel.Text = _quotes.Count == 1 ? "1 preventivo" : $"{_quotes.Count} preventivi";
            SubtitleLabel.Text = QuoteCountLabel.Text;
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(
                "Preventivi non disponibili",
                MobileDatabaseService.GetUserMessage(exception),
                "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadCustomersAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString) || _isLoading)
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
            await DisplayAlertAsync(
                "Clienti non disponibili",
                MobileDatabaseService.GetUserMessage(exception),
                "OK");
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
        bool quotesSelected = _activeSection == HomeSection.Quotes;
        QuotesPanel.IsVisible = quotesSelected && !string.IsNullOrWhiteSpace(_connectionString);
        CustomersPanel.IsVisible = !quotesSelected && !string.IsNullOrWhiteSpace(_connectionString);
        PageTitleLabel.Text = quotesSelected ? "Preventivi" : "Clienti";

        QuotesSectionButton.BackgroundColor = Color.FromArgb(quotesSelected ? "#B3261E" : "#FFFFFF");
        QuotesSectionButton.TextColor = Color.FromArgb(quotesSelected ? "#FFFFFF" : "#B3261E");
        CustomersSectionButton.BackgroundColor = Color.FromArgb(quotesSelected ? "#FFFFFF" : "#B3261E");
        CustomersSectionButton.TextColor = Color.FromArgb(quotesSelected ? "#B3261E" : "#FFFFFF");
    }

    private void SetBusy(bool isBusy)
    {
        _isLoading = isBusy;
        Busy.IsVisible = isBusy;
        Busy.IsRunning = isBusy;
    }
}
