using System.Collections.ObjectModel;
using System.Windows;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class PdfArchiveAuditWindow : Window
{
    private readonly PdfArchiveAuditService _auditService;
    private readonly ObservableCollection<PdfArchiveIssue> _issues = new();
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _restoreCts;
    private bool _isScanning;
    private bool _isRestoring;

    public PdfArchiveAuditWindow()
    {
        InitializeComponent();
        _auditService = new PdfArchiveAuditService(App.DataService, StoragePathService.Instance);
        GridIssues.ItemsSource = _issues;
        Loaded += async (_, _) => await ScanAsync();
        Closed += (_, _) =>
        {
            _scanCts?.Cancel();
            _restoreCts?.Cancel();
            _scanCts?.Dispose();
            _restoreCts?.Dispose();
            _scanCts = null;
            _restoreCts = null;
        };
    }

    private async Task ScanAsync()
    {
        if (_isScanning)
            return;

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = AppShutdownManager.CreateLinkedTokenSource();
        var token = _scanCts.Token;

        TxtStatus.Text = "Scansione in corso...";
        _issues.Clear();
        _isScanning = true;
        Cursor = System.Windows.Input.Cursors.Wait;

        try
        {
            var issues = await Task.Run(
                async () => await _auditService.ScanAsync(cancellationToken: token),
                token);
            token.ThrowIfCancellationRequested();

            foreach (var issue in issues)
                _issues.Add(issue);

            TxtStatus.Text = issues.Count == 0
                ? "Nessun problema trovato."
                : $"Trovati {issues.Count} problemi.";
        }
        catch (OperationCanceledException)
        {
            TxtStatus.Text = "Scansione annullata.";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "Scansione non riuscita.";
            MessageBox.Show($"Errore durante il controllo PDF.\n\n{ex.Message}", "Controllo PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cursor = null;
            _isScanning = false;
        }
    }

    private async void OnScanClick(object sender, RoutedEventArgs e)
    {
        await ScanAsync();
    }

    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if (_isRestoring)
            return;

        if (GridIssues.SelectedItem is not PdfArchiveIssue issue)
        {
            MessageBox.Show("Seleziona una riga da ripristinare.", "Controllo PDF", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!issue.CanRestore)
        {
            MessageBox.Show("Questo elemento non e' ripristinabile dal database: va rigenerato dal preventivo.", "Controllo PDF", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            _restoreCts?.Cancel();
            _restoreCts?.Dispose();
            _restoreCts = AppShutdownManager.CreateLinkedTokenSource();
            var token = _restoreCts.Token;
            _isRestoring = true;
            Cursor = System.Windows.Input.Cursors.Wait;
            TxtStatus.Text = $"Ripristino {issue.QuoteNumber}...";
            await Task.Run(
                async () => await _auditService.RestoreAsync(issue, token),
                token);
            token.ThrowIfCancellationRequested();

            _issues.Remove(issue);
            TxtStatus.Text = "Ripristino completato.";
        }
        catch (OperationCanceledException)
        {
            TxtStatus.Text = "Ripristino annullato.";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "Ripristino non riuscito.";
            MessageBox.Show($"Errore durante il ripristino.\n\n{ex.Message}", "Controllo PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Cursor = null;
            _isRestoring = false;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
