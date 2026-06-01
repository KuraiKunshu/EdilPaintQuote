using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using EdilPaintPreventibiviGen.ViewModels;
using EdilPaintPreventibiviGen.Helpers;

namespace EdilPaintPreventibiviGen.Views;

public partial class HistoryWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly QuoteHistoryService _historyService;
    private readonly int _initialHistoryCount;
    private bool _isLoadingHistory;
    private bool _isSavingStatus;
    private readonly HashSet<string> _loadedQuoteNumbers = new();
    private CancellationTokenSource? _searchCts;

    public ICollectionView HistoryView { get; private set; } = null!;

    public HistoryWindow(MainViewModel vm)
    {
        InitializeComponent();

        _vm = vm;
        _historyService = new QuoteHistoryService(App.DataService, StoragePathService.Instance);
        _initialHistoryCount = Math.Max(1, App.AppSettings.App.NumberOfQuote);

        DataContext = _vm;
        HistoryView = CollectionViewSource.GetDefaultView(_vm.HistorySummaries);
        if (HistoryView is ListCollectionView lcv)
        {
            lcv.CustomSort = null;
            lcv.SortDescriptions.Clear();
            lcv.SortDescriptions.Add(new SortDescription(nameof(QuoteHistorySummary.Date), ListSortDirection.Descending));
        }
        
        GridHistory.ItemsSource = HistoryView;

        PreviewKeyDown += HistoryWindow_PreviewKeyDown;
        Closed += HistoryWindow_Closed;

        _ = LoadInitialHistoryAsync();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void HistoryWindow_Closed(object? sender, EventArgs e)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        TxtSearchHistory.Text = string.Empty;
        CboFilterStatus.SelectedIndex = 0;
        DpStart.SelectedDate = null;
        DpEnd.SelectedDate = null;
    }

    private void HistoryWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = ExecuteSearchAsync();
        }
    }

    private void OnSearchButtonClick(object sender, RoutedEventArgs e)
    {
        _ = ExecuteSearchAsync();
    }

    private async Task ExecuteSearchAsync()
    {
        // Cancella qualsiasi ricerca precedente
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        // Previeni ricerche multiple simultanee
        if (_isLoadingHistory)
        {
            Debug.WriteLine("[ExecuteSearch] Ricerca gia' in corso, ignorata");
            return;
        }

        string searchText = TxtSearchHistory.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(searchText))
        {
            await LoadInitialHistoryAsync();
        }
        else
        {
            await RunSearchAsync(searchText, token);
        }
    }

    private async Task LoadInitialHistoryAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        if (_isLoadingHistory) return;

        try
        {
            _isLoadingHistory = true;
            Mouse.OverrideCursor = Cursors.Wait;
            LoadingOverlay.Visibility = Visibility.Visible;

            _loadedQuoteNumbers.Clear();

            Debug.WriteLine($"[LoadInitialHistory] Caricamento primi {_initialHistoryCount} preventivi...");

            await Task.Run(async () =>
            {
                await _vm.LoadHistorySummariesAsync(_initialHistoryCount, token);
            }, token);

            if (token.IsCancellationRequested)
            {
                Debug.WriteLine("[LoadInitialHistory] Operazione cancellata");
                return;
            }

            foreach (var summary in _vm.HistorySummaries)
                _loadedQuoteNumbers.Add(summary.QuoteNumber);

            Debug.WriteLine($"[LoadInitialHistory] Caricati {_vm.HistorySummaries.Count} preventivi");

            ApplyLocalFilters();
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[LoadInitialHistory] Task cancellato");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoadInitialHistory] Errore: {ex.Message}");
            MessageBox.Show($"Errore durante il caricamento: {ex.Message}", "Errore", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            Mouse.OverrideCursor = null;
            _isLoadingHistory = false;
        }
    }
    
    private async Task RunSearchAsync(string searchText, CancellationToken token)
    {
        if (_isLoadingHistory) return;

        try
        {
            _isLoadingHistory = true;
            Mouse.OverrideCursor = Cursors.Wait;
            LoadingOverlay.Visibility = Visibility.Visible;

            Debug.WriteLine($"[RunSearch] Inizio ricerca per: '{searchText}'");

            int take = Math.Max(1, App.AppSettings.App.NumberOfQuote);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

            var results = await Task.Run(async () =>
            {
                Debug.WriteLine("[RunSearch] Chiamata a SearchHistorySummariesAsync...");
                var data = await _vm.SearchHistorySummariesAsync(searchText, take, linkedCts.Token);
                Debug.WriteLine($"[RunSearch] Ricevuti {data?.Count ?? 0} risultati");
                return data;
            }, linkedCts.Token);

            if (token.IsCancellationRequested)
            {
                Debug.WriteLine("[RunSearch] Operazione cancellata");
                return;
            }

            _loadedQuoteNumbers.Clear();
            _vm.HistorySummaries.Clear();

            if (results != null)
            {
                foreach (var entry in results)
                {
                    _vm.HistorySummaries.Add(entry);
                    _loadedQuoteNumbers.Add(entry.QuoteNumber);
                }

                Debug.WriteLine($"[RunSearch] Aggiunti {results.Count} preventivi alla UI");
            }

            ApplyLocalFilters();
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[RunSearch] Task cancellato o timeout");
            MessageBox.Show("La ricerca e' stata annullata o ha superato il tempo limite (30 secondi).",
                "Ricerca Interrotta", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RunSearch] Errore: {ex.Message}\n{ex.StackTrace}");
            MessageBox.Show($"Errore durante la ricerca: {ex.Message}\n\nDettagli: {ex.GetType().Name}",
                "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            Mouse.OverrideCursor = null;
            _isLoadingHistory = false;
            Debug.WriteLine("[RunSearch] Ricerca completata");
        }
    }
    
    private void ApplyLocalFilters()
    {
        if (HistoryView == null) return;

        string selectedStatus = (CboFilterStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Tutti";

        HistoryView.Filter = item =>
        {
            if (item is not QuoteHistorySummary quote) return false;

            bool matchesStatus = selectedStatus == "Tutti" ||
                quote.Status.ToString().Equals(selectedStatus, StringComparison.OrdinalIgnoreCase);

            bool matchesDate = true;
            if (DpStart.SelectedDate.HasValue)
                matchesDate &= quote.Date.Date >= DpStart.SelectedDate.Value.Date;

            if (DpEnd.SelectedDate.HasValue)
                matchesDate &= quote.Date.Date <= DpEnd.SelectedDate.Value.Date;

            return matchesStatus && matchesDate;
        };

        HistoryView.Refresh();
    }

    private void OnFilterChanged(object sender, EventArgs e)
    {
        ApplyLocalFilters();
    }

    private async void OnCopyPastQuoteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not QuoteHistorySummary entry) return;

        var fullEntry = await _historyService.GetQuoteByNumberAsync(entry.QuoteNumber);
        if (fullEntry == null) return;

        if (MessageBox.Show($"Vuoi creare un NUOVO preventivo copiando i dati del n. {entry.QuoteNumber}?",
                "Copia Preventivo", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _vm.LoadQuoteFromHistory(fullEntry, isEdit: false);
            Close();
        }
    }

    private async void OnEditPastQuoteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not QuoteHistorySummary entry) return;

        var fullEntry = await _historyService.GetQuoteByNumberAsync(entry.QuoteNumber);
        if (fullEntry == null) return;

        if (MessageBox.Show($"Vuoi MODIFICARE il preventivo n. {entry.QuoteNumber}?",
                "Modifica Preventivo", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _vm.LoadQuoteFromHistory(fullEntry, isEdit: true);
            Close();
        }
    }

    private async void OnNotesClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not QuoteHistorySummary entry) return;

        var fullEntry = await _historyService.GetQuoteByNumberAsync(entry.QuoteNumber);
        if (fullEntry == null) return;

        var notesWin = new NotesWindow(fullEntry.Notes) { Owner = this };

        if (notesWin.ShowDialog() == true)
        {
            fullEntry.Notes = notesWin.ResultNotes;
            await _historyService.SaveSingleAsync(fullEntry);
            entry.Notes = fullEntry.Notes;
        }
    }

    private async void OnStatusChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingHistory) return;
        if (_isSavingStatus) return;

        if (e.RemovedItems.Count == 0) return;

        if (sender is not ComboBox cb || cb.DataContext is not QuoteHistorySummary summary) return;

        if (e.AddedItems.Count == 0 || e.RemovedItems.Count == 0) return;
        if (e.AddedItems[0] is not QuoteStatus newStatus) return;
        if (e.RemovedItems[0] is not QuoteStatus oldStatus) return;
        if (newStatus == oldStatus) return;

        if (!_loadedQuoteNumbers.Contains(summary.QuoteNumber)) return;

        try
        {
            _isSavingStatus = true;
            Mouse.OverrideCursor = Cursors.Wait;

            var fullEntry = await _historyService.GetQuoteByNumberAsync(summary.QuoteNumber);
            if (fullEntry == null) return;

            fullEntry.Status = newStatus;
            summary.Status = newStatus;

            // Salva solo i metadati: svuota i byte per evitare crash JSON su file grandi.
            if (fullEntry.PdfFile != null)
                fullEntry.PdfFile.Content = [];
            foreach (var att in fullEntry.Attachments)
                att.Content = [];

            await _historyService.SaveSingleAsync(fullEntry);
        }
        catch (Exception ex)
        {
            summary.Status = oldStatus;
            Debug.WriteLine($"[OnStatusChanged] Errore salvataggio stato: {ex.Message}");
            MessageBox.Show($"Errore durante il salvataggio dello stato: {ex.Message}",
                "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            _isSavingStatus = false;
        }
    }

    private async void OnDeletePastQuoteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not QuoteHistorySummary entry) return;

        var fullEntry = await _historyService.GetQuoteByNumberAsync(entry.QuoteNumber);
        if (fullEntry == null) return;

        if (MessageBox.Show($"Sei sicuro di voler eliminare definitivamente il preventivo n. {entry.QuoteNumber}?\n\nQuesta operazione cancellera' anche il file PDF.",
                "Conferma Eliminazione", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            _historyService.DeleteQuoteFiles(fullEntry);
            await _historyService.DeleteQuoteAsync(fullEntry.QuoteNumber);
            _vm.HistorySummaries.Remove(entry);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante l'eliminazione: {ex.Message}", "Avviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void OnOpenPastQuotePdfClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not QuoteHistorySummary entry) return;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;

            var fullEntry = await _historyService.GetQuoteByNumberAsync(entry.QuoteNumber);
            if (fullEntry == null) return;

            string expectedPath = _historyService.GetExpectedPdfPath(fullEntry);

            // Prima fonte: PDF ufficiale salvato nel DB. Se il file locale e' diverso, viene riscritto.
            var officialPath = await _historyService.EnsureOfficialPdfExistsAsync(fullEntry);
            if (!string.IsNullOrWhiteSpace(officialPath) && File.Exists(officialPath))
            {
                Process.Start(new ProcessStartInfo { FileName = officialPath, UseShellExecute = true });
                return;
            }

            // Fallback: usa eventuali file locali solo se il DB non contiene ancora il PDF ufficiale.
            if (!string.IsNullOrWhiteSpace(fullEntry.PdfPath) && File.Exists(fullEntry.PdfPath))
            {
                Process.Start(new ProcessStartInfo { FileName = fullEntry.PdfPath, UseShellExecute = true });
                return;
            }

            if (File.Exists(expectedPath))
            {
                Process.Start(new ProcessStartInfo { FileName = expectedPath, UseShellExecute = true });
                return;
            }

            var foundPath = _historyService.FindPdfByQuoteNumber(fullEntry);
            if (!string.IsNullOrWhiteSpace(foundPath) && File.Exists(foundPath))
            {
                Process.Start(new ProcessStartInfo { FileName = foundPath, UseShellExecute = true });
                return;
            }

            if (MessageBox.Show(
                    $"Il file PDF del preventivo n. {entry.QuoteNumber} non e' stato trovato nel DB ne' nella cartella condivisa.\nVuoi rigenerarlo con i dati originali?",
                    "File non trovato", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            _vm.LoadQuoteFromHistory(fullEntry, isEdit: true);

            await _vm.GeneratePdfAsync(
                incrementCounter: false,
                openAfterGeneration: false,
                specificDate: fullEntry.Date,
                forceTargetPath: expectedPath);

            var regeneratedOfficialPath = await _historyService.EnsureOfficialPdfExistsAsync(fullEntry);
            var pathToOpen = !string.IsNullOrWhiteSpace(regeneratedOfficialPath) ? regeneratedOfficialPath : expectedPath;

            if (File.Exists(pathToOpen))
            {
                Process.Start(new ProcessStartInfo { FileName = pathToOpen, UseShellExecute = true });
            }
            else
            {
                MessageBox.Show("Il PDF e' stato rigenerato ma non e' stato possibile aprirlo automaticamente.",
                    "Avviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossibile aprire il preventivo: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
    
    private void OnOpenCustomerFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not QuoteHistorySummary entry)
            return;

        if (string.IsNullOrWhiteSpace(entry.CustomerName))
        {
            MessageBox.Show("Nessun cliente associato a questo preventivo.",
                "Cartella non disponibile", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            string referenceName = string.IsNullOrWhiteSpace(entry.ReferenceName)
                ? null!
                : entry.ReferenceName;

            string folder = StoragePathService.Instance.BuildCustomerPdfFolder(
                entry.CustomerName, referenceName);

            StoragePathService.Instance.OpenFolder(folder);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossibile aprire la cartella.\n\n{ex.Message}",
                "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
            
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
