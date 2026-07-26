using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using EdilPaintPreventibiviGen.ViewModels;

namespace EdilPaintPreventibiviGen.Views;

public partial class SuppliersWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly QuoteHistoryService _historyService;
    private readonly ObservableCollection<QuoteHistorySummary> _quotes = new();
    private CancellationTokenSource? _refreshCts;
    private bool _isRefreshing;
    private bool _isSaving;

    public IReadOnlyList<string> MaterialStatusOptions { get; } =
    [
        "Da ordinare",
        "Ordinato",
        "Da ritirare",
        "In magazzino",
        "Consegnato",
        "Non disponibile"
    ];

    public SuppliersWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _historyService = new QuoteHistoryService(App.DataService, StoragePathService.Instance);
        GridSuppliers.ItemsSource = _quotes;
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) =>
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = null;
        };
    }

    private async Task RefreshAsync(string searchText = "")
    {
        if (_isRefreshing)
            return;

        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = AppShutdownManager.CreateLinkedTokenSource();
        var token = _refreshCts.Token;

        try
        {
            _isRefreshing = true;
            Cursor = Cursors.Wait;
            TxtSubtitle.Text = "Caricamento...";
            EmptyPanel.Visibility = Visibility.Collapsed;

            int take = Math.Clamp(App.AppSettings.App.NumberOfQuote <= 0 ? 100 : App.AppSettings.App.NumberOfQuote, 1, 250);
            var summaries = await _historyService.LoadSupplierOrderSummariesAsync(
                searchText,
                take,
                token);

            token.ThrowIfCancellationRequested();

            _quotes.Clear();
            foreach (var summary in summaries)
                _quotes.Add(summary);

            TxtSubtitle.Text = $"{_quotes.Count} ordini inviati o modificati";
            EmptyPanel.Visibility = _quotes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            TxtSubtitle.Text = "Caricamento non riuscito.";
            MessageBox.Show(
                $"Errore durante il caricamento fornitori.\n\n{ex.Message}",
                "Fornitori",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            Cursor = null;
            _isRefreshing = false;
        }
    }

    private async Task SaveSupplierAsync(QuoteHistorySummary summary)
    {
        if (_isSaving)
            return;

        try
        {
            _isSaving = true;
            Cursor = Cursors.Wait;
            CommitPendingEdits();

            string deviceName = DeviceNameService.GetCurrentDeviceName();
            await _historyService.UpdateSupplierInfoAsync(summary.QuoteNumber, new QuoteSupplierInfo
            {
                SupplierName = summary.SupplierName,
                MaterialOrderDate = summary.MaterialOrderDate,
                ExpectedDeliveryDate = summary.ExpectedDeliveryDate,
                MaterialStatus = summary.MaterialStatus,
                DeviceName = deviceName
            });

            summary.LastModifiedByDevice = deviceName;
            TxtSubtitle.Text = $"Salvato preventivo {summary.QuoteNumber}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Suppliers] Errore salvataggio {summary.QuoteNumber}: {ex.Message}");
            MessageBox.Show(
                $"Errore durante il salvataggio del preventivo {summary.QuoteNumber}.\n\n{ex.Message}",
                "Fornitori",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            Cursor = null;
            _isSaving = false;
        }
    }

    private void SelectSupplier(QuoteHistorySummary summary)
    {
        var win = new SelectCustomerWindow(_vm, suppliersOnly: true)
        {
            Owner = this,
            Title = "Seleziona fornitore"
        };

        if (win.ShowDialog() != true || win.SelectedResult == null)
            return;

        summary.SupplierName = win.SelectedResult.BusinessName;
    }

    private async Task PrepareOrderMailAsync(QuoteHistorySummary summary)
    {
        try
        {
            CommitPendingEdits();
            var fullEntry = await _historyService.GetQuoteByNumberAsync(summary.QuoteNumber);
            if (fullEntry == null)
            {
                MessageBox.Show(
                    "Preventivo non trovato nello storico.",
                    "Fornitori",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            fullEntry.SupplierName = summary.SupplierName;
            fullEntry.MaterialOrderDate = summary.MaterialOrderDate;
            fullEntry.ExpectedDeliveryDate = summary.ExpectedDeliveryDate;
            fullEntry.MaterialStatus = summary.MaterialStatus;

            if (string.IsNullOrWhiteSpace(fullEntry.SupplierName))
            {
                MessageBox.Show(
                    "Seleziona prima un fornitore.",
                    "Fornitori",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var draft = SupplierOrderMailService.CreateDraft(fullEntry, _vm.AllCustomers);
            if (string.IsNullOrWhiteSpace(draft.Recipient))
            {
                MessageBox.Show(
                    "Il fornitore selezionato non ha un indirizzo email in anagrafica. La finestra verra' preparata senza destinatario.",
                    "Fornitori",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            var win = new SupplierOrderMailWindow(fullEntry, draft) { Owner = this };
            if (win.ShowDialog() == true && win.WasRegisteredAsSent)
            {
                summary.MaterialOrderDate ??= win.RegisteredAtUtc.ToLocalTime().Date;
                if (string.IsNullOrWhiteSpace(summary.MaterialStatus))
                    summary.MaterialStatus = "Ordinato";

                await SaveSupplierAsync(summary);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Suppliers] Errore mail ordine {summary.QuoteNumber}: {ex.Message}");
            MessageBox.Show(
                $"Errore durante la gestione dell'ordine.\n\n{ex.Message}",
                "Fornitori",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void CommitPendingEdits()
    {
        if (Keyboard.FocusedElement is TextBox textBox)
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        if (Keyboard.FocusedElement is ComboBox comboBox)
            comboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();

        GridSuppliers.CommitEdit(DataGridEditingUnit.Cell, true);
        GridSuppliers.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private async void OnSaveSupplierClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is QuoteHistorySummary summary)
            await SaveSupplierAsync(summary);
    }

    private void OnSelectSupplierClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is QuoteHistorySummary summary)
            SelectSupplier(summary);
    }

    private async void OnPrepareOrderMailClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is QuoteHistorySummary summary)
            await PrepareOrderMailAsync(summary);
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e)
    {
        await RefreshAsync(TxtSearch.Text?.Trim() ?? string.Empty);
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        TxtSearch.Text = string.Empty;
        await RefreshAsync();
    }

    private async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        await RefreshAsync(TxtSearch.Text?.Trim() ?? string.Empty);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
