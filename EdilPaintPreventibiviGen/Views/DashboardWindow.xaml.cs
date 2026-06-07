using System.Windows;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class DashboardWindow : Window
{
    private const int MaxDashboardSummaryRows = 500;

    private readonly QuoteHistoryService _historyService;
    private readonly PdfArchiveAuditService _auditService;
    private readonly LocalDraftService _draftService;
    private CancellationTokenSource? _refreshCts;
    private bool _isRefreshing;

    public DashboardWindow()
    {
        InitializeComponent();
        _historyService = new QuoteHistoryService(App.DataService, StoragePathService.Instance);
        _auditService = new PdfArchiveAuditService(App.DataService, StoragePathService.Instance);
        _draftService = new LocalDraftService(LocalApplicationDataService.GetDataDirectoryPath());
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) =>
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = null;
        };
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing)
            return;

        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = AppShutdownManager.CreateLinkedTokenSource();
        var token = _refreshCts.Token;

        TxtSubtitle.Text = $"PC: {DeviceNameService.GetCurrentDeviceName()}";
        TxtTotalQuotes.Text = "...";
        TxtToRemind.Text = "...";
        TxtPdfIssues.Text = "...";
        TxtDraftState.Text = "...";
        _isRefreshing = true;
        Cursor = System.Windows.Input.Cursors.Wait;

        try
        {
            var diagnostics = await Task.Run(DiagnosticsService.CreateSnapshot, token);
            token.ThrowIfCancellationRequested();

            TxtDevice.Text = $"Nome dispositivo: {DeviceNameService.GetCurrentDeviceName()}";
            TxtDatabase.Text = $"Database: {diagnostics.DatabaseStatus}";
            TxtSync.Text = $"Sync: {diagnostics.SyncStatus} - ultima: {diagnostics.LastSync}";
            TxtPending.Text = $"Code locali: patch {diagnostics.PendingQuotePatches}, eliminazioni {diagnostics.PendingQuoteDeletes + diagnostics.PendingCustomerDeletes}";

            int summaryTake = Math.Clamp(
                App.AppSettings.App.NumberOfQuote <= 0 ? MaxDashboardSummaryRows : App.AppSettings.App.NumberOfQuote,
                100,
                MaxDashboardSummaryRows);

            var summaries = await Task.Run(
                async () => await _historyService.LoadTopSummariesAsync(summaryTake, token),
                token);
            token.ThrowIfCancellationRequested();

            var toRemind = summaries
                .Where(x => x.ShouldRemind || x.Status == QuoteStatus.DaSollecitare)
                .OrderBy(x => x.SentAtUtc ?? x.Date.DateTime)
                .ToList();

            var draftTask = _draftService.LoadAsync(token);
            var pdfIssuesTask = Task.Run(
                async () => await _auditService.ScanAsync(300, token),
                token);

            await Task.WhenAll(draftTask, pdfIssuesTask);
            token.ThrowIfCancellationRequested();

            var draft = await draftTask;
            var pdfIssues = await pdfIssuesTask;

            TxtTotalQuotes.Text = summaries.Count >= summaryTake
                ? $"{summaries.Count}+"
                : summaries.Count.ToString();
            TxtToRemind.Text = toRemind.Count.ToString();
            TxtPdfIssues.Text = pdfIssues.Count.ToString();
            TxtDraftState.Text = draft == null ? "Nessuna" : draft.LastModifiedUtc.ToLocalTime().ToString("dd/MM HH:mm");
            GridReminders.ItemsSource = toRemind.Take(100).ToList();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            TxtSubtitle.Text = "Aggiornamento dashboard non riuscito.";
            MessageBox.Show($"Errore durante l'aggiornamento dashboard.\n\n{ex.Message}",
                "Dashboard", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Cursor = null;
            _isRefreshing = false;
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        if (App.MainVm == null)
        {
            MessageBox.Show(
                "Lo storico non e' ancora disponibile. Riapri la finestra tra qualche secondo.",
                "Storico preventivi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var win = new HistoryWindow(App.MainVm) { Owner = this };
        win.ShowDialog();
    }

    private void OnPdfAuditClick(object sender, RoutedEventArgs e)
    {
        var win = new PdfArchiveAuditWindow { Owner = this };
        win.ShowDialog();
    }

    private void OnDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        var win = new DiagnosticsWindow { Owner = this };
        win.ShowDialog();
    }

    private async void OnSyncNowClick(object sender, RoutedEventArgs e)
    {
        if (App.SyncService.IsSyncRunning)
        {
            MessageBox.Show(
                "La sincronizzazione e' gia' in corso.",
                "Sincronizzazione",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            TxtSync.Text = "Sync: in corso...";
            var result = await App.SyncService.SyncAllAsync(force: true);
            string lastSync = App.SyncService.LastSyncCompletedUtc.HasValue
                ? App.SyncService.LastSyncCompletedUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")
                : "Mai";
            TxtSync.Text = string.IsNullOrWhiteSpace(result.Error)
                ? $"Sync: {App.SyncService.LastSyncSummary} - ultima: {lastSync}"
                : $"Sync: errore - {result.Error}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Sincronizzazione non riuscita.\n\n{ex.Message}",
                "Sincronizzazione",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            Cursor = null;
        }
    }
}
