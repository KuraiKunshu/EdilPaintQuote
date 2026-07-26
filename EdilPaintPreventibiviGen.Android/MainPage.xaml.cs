using System.Collections.ObjectModel;
using EdilPaintPreventibiviGen.Android.Models;
using EdilPaintPreventibiviGen.Android.Services;

namespace EdilPaintPreventibiviGen.Android;

public partial class MainPage : ContentPage
{
    private readonly CredentialStore _credentialStore = new();
    private readonly ObservableCollection<QuoteSummary> _quotes = new();
    private string _connectionString = string.Empty;
    private CancellationTokenSource? _searchCts;

    public MainPage()
    {
        InitializeComponent();
        QuoteList.ItemsSource = _quotes;
        StatusPicker.ItemsSource = QuoteStatusOptions.All.ToList();
        StatusPicker.SelectedIndex = 0;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadSavedCredentialsAsync();
        }
        catch (Exception ex)
        {
            _connectionString = string.Empty;
            _credentialStore.ClearConnectionString();
            ShowSetup();
            await DisplayAlertAsync(
                "Accesso salvato non disponibile",
                $"Le credenziali salvate sono state cancellate da questo dispositivo.\n\n{ex.GetBaseException().Message}",
                "OK");
        }
    }

    private async Task LoadSavedCredentialsAsync()
    {
        _connectionString = await _credentialStore.GetConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            ShowSetup();
            return;
        }

        ShowQuotes();
        await LoadQuotesAsync();
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
            await new QuoteReadService().TestConnectionAsync(value);
            await _credentialStore.SaveConnectionStringAsync(value);
            _connectionString = value;
            ConnectionStringEntry.Text = string.Empty;
            ShowQuotes();
            await LoadQuotesAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Connessione non riuscita", ex.GetBaseException().Message, "OK");
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
        _connectionString = string.Empty;
        _credentialStore.ClearConnectionString();
        ShowSetup();
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await LoadQuotesAsync();

    private async void OnRefreshViewRefreshing(object? sender, EventArgs e)
    {
        await LoadQuotesAsync();
        Refresh.IsRefreshing = false;
    }

    private async void OnSearchRequested(object? sender, EventArgs e) => await LoadQuotesAsync();

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, token);
                await MainThread.InvokeOnMainThreadAsync(LoadQuotesAsync);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private async void OnStatusChanged(object? sender, EventArgs e) => await LoadQuotesAsync();

    private async void OnQuoteSelected(object? sender, SelectionChangedEventArgs e)
    {
        var quote = e.CurrentSelection.FirstOrDefault() as QuoteSummary;
        QuoteList.SelectedItem = null;

        if (quote == null)
            return;

        try
        {
            SetBusy(true);
            var detail = await new QuoteReadService().GetDetailAsync(_connectionString, quote.QuoteNumber);
            await Navigation.PushAsync(new QuoteDetailPage(detail));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Dettaglio non disponibile", ex.GetBaseException().Message, "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadQuotesAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            return;

        try
        {
            SetBusy(true);
            string search = SearchBox.Text?.Trim() ?? string.Empty;
            var status = StatusPicker.SelectedItem as QuoteStatusOption;
            var quotes = await new QuoteReadService().GetSummariesAsync(
                _connectionString,
                search,
                status?.Value);

            _quotes.Clear();
            foreach (var quote in quotes)
                _quotes.Add(quote);

            SubtitleLabel.Text = $"{_quotes.Count} preventivi";
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Preventivi non disponibili", ex.GetBaseException().Message, "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowSetup()
    {
        SubtitleLabel.Text = "Connessione a Neon";
        SetupPanel.IsVisible = true;
        QuotesPanel.IsVisible = false;
        ForgetButton.IsVisible = false;
    }

    private void ShowQuotes()
    {
        SetupPanel.IsVisible = false;
        QuotesPanel.IsVisible = true;
        ForgetButton.IsVisible = true;
    }

    private void SetBusy(bool isBusy)
    {
        Busy.IsVisible = isBusy;
        Busy.IsRunning = isBusy;
    }
}
