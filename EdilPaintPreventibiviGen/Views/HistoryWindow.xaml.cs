using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
    private const int MaxVisibleHistoryRows = 250;

    private readonly MainViewModel _vm;
    private readonly QuoteHistoryService _historyService;
    private readonly int _initialHistoryCount;
    private bool _isLoadingHistory;
    private bool _isSavingStatus;
    private readonly HashSet<string> _loadedQuoteNumbers = new();
    private readonly List<QuoteHistorySummary> _currentSummaries = new();
    private CancellationTokenSource? _searchCts;

    public ICollectionView HistoryView { get; private set; } = null!;

    public HistoryWindow(MainViewModel vm)
    {
        InitializeComponent();

        _vm = vm;
        _historyService = new QuoteHistoryService(App.DataService, StoragePathService.Instance);
        _initialHistoryCount = NormalizeHistoryTake(App.AppSettings.App.NumberOfQuote);

        DataContext = _vm;
        HistoryView = CollectionViewSource.GetDefaultView(_vm.HistorySummaries);
        GridHistory.ItemsSource = _vm.HistorySummaries;

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
        _searchCts = AppShutdownManager.CreateLinkedTokenSource();
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
        _searchCts = AppShutdownManager.CreateLinkedTokenSource();
        var token = _searchCts.Token;

        if (_isLoadingHistory) return;

        try
        {
            _isLoadingHistory = true;
            Mouse.OverrideCursor = Cursors.Wait;
            LoadingOverlay.Visibility = Visibility.Visible;

            _loadedQuoteNumbers.Clear();

            Debug.WriteLine($"[LoadInitialHistory] Caricamento primi {_initialHistoryCount} preventivi...");

            var summaries = await _historyService.LoadTopSummariesAsync(_initialHistoryCount, token);

            if (token.IsCancellationRequested)
            {
                Debug.WriteLine("[LoadInitialHistory] Operazione cancellata");
                return;
            }

            SetCurrentHistorySummaries(summaries);

            Debug.WriteLine($"[LoadInitialHistory] Caricati {_vm.HistorySummaries.Count} preventivi");
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

            int take = NormalizeHistoryTake(App.AppSettings.App.NumberOfQuote);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

            Debug.WriteLine("[RunSearch] Chiamata a SearchHistorySummariesAsync...");
            var results = await _vm.SearchHistorySummariesAsync(searchText, take, linkedCts.Token);
            Debug.WriteLine($"[RunSearch] Ricevuti {results?.Count ?? 0} risultati");

            if (token.IsCancellationRequested)
            {
                Debug.WriteLine("[RunSearch] Operazione cancellata");
                return;
            }

            SetCurrentHistorySummaries(results ?? []);
            Debug.WriteLine($"[RunSearch] Aggiunti {results?.Count ?? 0} preventivi alla UI");
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[RunSearch] Task cancellato o timeout");
            if (!token.IsCancellationRequested)
            {
                MessageBox.Show("La ricerca ha superato il tempo limite (30 secondi).",
                    "Ricerca interrotta", MessageBoxButton.OK, MessageBoxImage.Information);
            }
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
        if (!IsInitialized || GridHistory == null)
            return;

        string selectedStatus = (CboFilterStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Tutti";

        var filtered = _currentSummaries.Where(quote =>
        {
            bool matchesStatus = selectedStatus == "Tutti" ||
                quote.Status.ToString().Equals(selectedStatus, StringComparison.OrdinalIgnoreCase);

            bool matchesDate = true;
            if (DpStart.SelectedDate.HasValue)
                matchesDate &= quote.Date.Date >= DpStart.SelectedDate.Value.Date;

            if (DpEnd.SelectedDate.HasValue)
                matchesDate &= quote.Date.Date <= DpEnd.SelectedDate.Value.Date;

            return matchesStatus && matchesDate;
        }).ToList();

        ReplaceVisibleHistorySummaries(filtered);
    }

    private static int NormalizeHistoryTake(int configuredTake)
        => Math.Clamp(configuredTake <= 0 ? 100 : configuredTake, 1, MaxVisibleHistoryRows);

    private void SetCurrentHistorySummaries(IEnumerable<QuoteHistorySummary> summaries)
    {
        _currentSummaries.Clear();
        _currentSummaries.AddRange(summaries);
        ApplyLocalFilters();
    }

    private void ReplaceVisibleHistorySummaries(IEnumerable<QuoteHistorySummary> summaries)
    {
        _loadedQuoteNumbers.Clear();

        GridHistory.ItemsSource = null;
        _vm.HistorySummaries.Clear();
        foreach (var summary in summaries)
        {
            _vm.HistorySummaries.Add(summary);
            _loadedQuoteNumbers.Add(summary.QuoteNumber);
        }
        GridHistory.ItemsSource = _vm.HistorySummaries;
    }

    private void OnFilterChanged(object sender, EventArgs e)
    {
        ApplyLocalFilters();
    }

    private async void OnCopyPastQuoteClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSummary(sender, out var entry)) return;

        await CopyPastQuoteAsync(entry);
    }

    private async Task CopyPastQuoteAsync(QuoteHistorySummary entry)
    {
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
        if (!TryGetSummary(sender, out var entry)) return;

        await EditPastQuoteAsync(entry);
    }

    private async Task EditPastQuoteAsync(QuoteHistorySummary entry)
    {
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
        if (!TryGetSummary(sender, out var entry)) return;

        await EditNotesAsync(entry);
    }

    private async Task EditNotesAsync(QuoteHistorySummary entry)
    {
        var notesWin = new NotesWindow(entry.Notes) { Owner = this };

        if (notesWin.ShowDialog() == true)
        {
            await _historyService.UpdateNotesAsync(entry.QuoteNumber, notesWin.ResultNotes);
            entry.Notes = notesWin.ResultNotes;
        }
    }

    private async void OnSendQuoteClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSummary(sender, out var entry)) return;

        await SendQuoteAsync(entry);
    }

    private async Task SendQuoteAsync(QuoteHistorySummary entry)
    {
        var win = new QuoteSendWindow(
            entry,
            ResolveDefaultRecipient(entry),
            FormatMailTemplate(App.AppSettings.Mail.DefaultSubject, entry),
            FormatMailTemplate(App.AppSettings.Mail.DefaultBody, entry),
            ResolveDefaultWhatsAppPhone(entry))
        {
            Owner = this
        };

        if (win.ShowDialog() != true)
            return;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;

            SmtpEmailSendResult? emailResult = null;
            if (App.AppSettings.Mail.Enabled)
            {
                emailResult = await SendQuoteEmailAsync(entry, win);
            }
            else if (MessageBox.Show(
                "L'invio email SMTP non e' abilitato nelle impostazioni.\nVuoi registrare comunque l'invio manualmente?",
                "Invio email",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            await _historyService.UpdateSendInfoAsync(entry.QuoteNumber, win.Result);
            entry.Status = QuoteStatus.Spedito;
            entry.SentAtUtc = win.Result.SentAtUtc;
            entry.SentMethod = win.Result.Method;
            entry.SentRecipient = win.Result.Recipient;
            entry.SentByDevice = win.Result.DeviceName;
            entry.LastModifiedByDevice = win.Result.DeviceName;
            ApplyLocalFilters();

            if (emailResult != null)
            {
                MessageBox.Show(
                    "Preventivo inviato correttamente.",
                    "Invio preventivo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            if (win.ShouldOpenWhatsApp)
                TryOpenWhatsAppMessage(win);
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show("Invio email annullato o interrotto per timeout.",
                "Invio preventivo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SendQuote] Errore invio/salvataggio: {ex}");
            MessageBox.Show("Preventivo non inviato. Controlla il log SMTP per i dettagli.",
                "Invio preventivo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async Task<SmtpEmailSendResult> SendQuoteEmailAsync(QuoteHistorySummary entry, QuoteSendWindow win)
    {
        using var emailCts = AppShutdownManager.CreateLinkedTokenSource();
        emailCts.CancelAfter(TimeSpan.FromSeconds(45));
        var token = emailCts.Token;

        string pdfPath = await ResolvePdfPathForEmailAsync(entry, token);
        var service = new SmtpEmailService(App.AppSettings.Mail);
        return await service.SendAsync(new SmtpEmailRequest
        {
            Recipient = win.PrimaryEmailRecipient,
            CcRecipients = win.EmailCcRecipients,
            Subject = win.EmailSubject,
            Body = win.EmailBody,
            AttachmentPath = pdfPath
        }, token);
    }

    private void TryOpenWhatsAppMessage(QuoteSendWindow win)
    {
        try
        {
            OpenWhatsAppMessage(win.WhatsAppPhone, win.WhatsAppMessage);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SendQuote] Impossibile aprire WhatsApp: {ex}");
            MessageBox.Show(
                "Preventivo inviato, ma non sono riuscito ad aprire WhatsApp.",
                "WhatsApp",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static void OpenWhatsAppMessage(string phone, string message)
    {
        string digits = new(phone.Where(char.IsDigit).ToArray());
        string encodedMessage = Uri.EscapeDataString(message ?? string.Empty);
        string url = string.IsNullOrWhiteSpace(digits)
            ? $"https://wa.me/?text={encodedMessage}"
            : $"https://wa.me/{digits}?text={encodedMessage}";

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async Task<string> ResolvePdfPathForEmailAsync(
        QuoteHistorySummary entry,
        CancellationToken cancellationToken)
    {
        var fullEntry = await _historyService.GetQuoteByNumberAsync(entry.QuoteNumber)
            ?? throw new InvalidOperationException("Preventivo non trovato nello storico.");

        string officialPath = await _historyService.EnsureOfficialPdfExistsAsync(fullEntry, cancellationToken);
        if (!string.IsNullOrWhiteSpace(officialPath) && File.Exists(officialPath))
            return officialPath;

        if (!string.IsNullOrWhiteSpace(fullEntry.PdfPath) && File.Exists(fullEntry.PdfPath))
            return fullEntry.PdfPath;

        string expectedPath = _historyService.GetExpectedPdfPath(fullEntry);
        if (File.Exists(expectedPath))
            return expectedPath;

        string? foundPath = _historyService.FindPdfByQuoteNumber(fullEntry);
        if (!string.IsNullOrWhiteSpace(foundPath) && File.Exists(foundPath))
            return foundPath;

        throw new InvalidOperationException(
            $"PDF del preventivo n. {entry.QuoteNumber} non trovato. Apri o rigenera prima il PDF del preventivo.");
    }

    private string ResolveDefaultRecipient(QuoteHistorySummary entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.SentRecipient))
            return entry.SentRecipient;

        return ResolveCustomer(entry)?.Email ?? string.Empty;
    }

    private string ResolveDefaultWhatsAppPhone(QuoteHistorySummary entry) =>
        ResolveCustomer(entry)?.Phone ?? string.Empty;

    private Customer? ResolveCustomer(QuoteHistorySummary entry) =>
        _vm.AllCustomers.FirstOrDefault(customer => string.Equals(
            customer.BusinessName,
            entry.CustomerName,
            StringComparison.OrdinalIgnoreCase));

    private static string FormatMailTemplate(string template, QuoteHistorySummary entry)
    {
        string text = string.IsNullOrWhiteSpace(template) ? string.Empty : template;
        return text
            .Replace("{QuoteNumber}", entry.QuoteNumber, StringComparison.OrdinalIgnoreCase)
            .Replace("{CustomerName}", entry.CustomerName, StringComparison.OrdinalIgnoreCase)
            .Replace("{ReferenceName}", BlankToDash(entry.ReferenceName), StringComparison.OrdinalIgnoreCase)
            .Replace("{Date}", entry.Date.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("it-IT")), StringComparison.OrdinalIgnoreCase)
            .Replace("{Total}", entry.Total.ToString("N2", CultureInfo.GetCultureInfo("it-IT")), StringComparison.OrdinalIgnoreCase);
    }

    private async void OnReminderClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSummary(sender, out var entry)) return;

        await RegisterReminderAsync(entry);
    }

    private async Task RegisterReminderAsync(QuoteHistorySummary entry)
    {
        if (MessageBox.Show($"Registrare un sollecito per il preventivo n. {entry.QuoteNumber}?",
                "Sollecito", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var reminder = new QuoteReminderInfo
        {
            ReminderAtUtc = DateTime.UtcNow,
            DeviceName = DeviceNameService.GetCurrentDeviceName()
        };

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            await _historyService.RegisterReminderAsync(entry.QuoteNumber, reminder);
            entry.Status = QuoteStatus.Spedito;
            entry.LastReminderAtUtc = reminder.ReminderAtUtc;
            entry.ReminderCount += 1;
            entry.LastReminderByDevice = reminder.DeviceName;
            entry.LastModifiedByDevice = reminder.DeviceName;
            ApplyLocalFilters();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante il salvataggio del sollecito: {ex.Message}",
                "Sollecito", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
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

            summary.Status = newStatus;
            await _historyService.UpdateStatusAsync(summary.QuoteNumber, newStatus);
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
        if (!TryGetSummary(sender, out var entry)) return;

        await DeletePastQuoteAsync(entry);
    }

    private async Task DeletePastQuoteAsync(QuoteHistorySummary entry)
    {
        var fullEntry = await _historyService.GetQuoteByNumberAsync(entry.QuoteNumber);
        if (fullEntry == null) return;

        if (MessageBox.Show($"Sei sicuro di voler eliminare definitivamente il preventivo n. {entry.QuoteNumber}?\n\nQuesta operazione cancellera' anche il file PDF.",
                "Conferma Eliminazione", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            _historyService.DeleteQuoteFiles(fullEntry);
            await _historyService.DeleteQuoteAsync(fullEntry.QuoteNumber);
            _currentSummaries.Remove(entry);
            _vm.HistorySummaries.Remove(entry);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante l'eliminazione: {ex.Message}", "Avviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void OnOpenPastQuotePdfClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSummary(sender, out var entry)) return;

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
    
    private async void OnOpenCustomerFolderClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSummary(sender, out var entry)) return;

        await OpenCustomerFolderAsync(entry);
    }

    private async Task OpenCustomerFolderAsync(QuoteHistorySummary entry)
    {
        if (string.IsNullOrWhiteSpace(entry.CustomerName))
        {
            MessageBox.Show("Nessun cliente associato a questo preventivo.",
                "Cartella non disponibile", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var fullEntry = await _historyService.GetQuoteByNumberAsync(entry.QuoteNumber);
            if (fullEntry != null)
                await _historyService.EnsureAttachmentsFolderExistsAsync(fullEntry);

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

    private void OnMoreActionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not QuoteHistorySummary entry)
            return;

        var menu = new ContextMenu
        {
            PlacementTarget = btn
        };

        menu.Items.Add(CreateDisabledMenuItem($"Preventivo {entry.QuoteNumber}"));
        menu.Items.Add(CreateDisabledMenuItem($"IVA: {entry.IvaDisplay}"));
        menu.Items.Add(CreateDisabledMenuItem($"Sconti: {entry.DiscountDisplay}"));
        menu.Items.Add(CreateDisabledMenuItem($"Invio: {entry.SentDisplay}"));
        menu.Items.Add(CreateDisabledMenuItem($"Solleciti: {entry.ReminderDisplay}"));
        menu.Items.Add(CreateDisabledMenuItem($"PC: {BlankToDash(entry.LastModifiedByDevice)}"));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Copia in nuovo preventivo", async () => await CopyPastQuoteAsync(entry)));
        menu.Items.Add(CreateMenuItem("Apri cartella cliente", async () => await OpenCustomerFolderAsync(entry)));
        menu.Items.Add(CreateMenuItem("Invia / registra invio", async () => await SendQuoteAsync(entry)));
        menu.Items.Add(CreateMenuItem("Registra sollecito", async () => await RegisterReminderAsync(entry)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Elimina preventivo", async () => await DeletePastQuoteAsync(entry)));

        btn.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private static bool TryGetSummary(object sender, out QuoteHistorySummary entry)
    {
        if (sender is FrameworkElement element && element.DataContext is QuoteHistorySummary summary)
        {
            entry = summary;
            return true;
        }

        entry = null!;
        return false;
    }

    private static MenuItem CreateDisabledMenuItem(string header)
    {
        return new MenuItem
        {
            Header = header,
            IsEnabled = false
        };
    }

    private static MenuItem CreateMenuItem(string header, Func<Task> action)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) => await action();
        return item;
    }

    private static string BlankToDash(string value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value;
            
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
