using System.Windows;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class SentOpenQuotesWindow : Window
{
    private readonly QuoteHistoryService _historyService;
    private CancellationTokenSource? _refreshCts;
    private bool _isRefreshing;

    public SentOpenQuotesWindow()
    {
        InitializeComponent();
        _historyService = new QuoteHistoryService(App.DataService, StoragePathService.Instance);
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

        _isRefreshing = true;
        Cursor = System.Windows.Input.Cursors.Wait;
        TxtSubtitle.Text = "Caricamento preventivi inviati negli ultimi 2 mesi...";
        TxtEmpty.Visibility = Visibility.Collapsed;

        try
        {
            DateTime sinceUtc = DateTime.UtcNow.AddMonths(-2);
            var summaries = await _historyService.LoadSentOpenSummariesAsync(sinceUtc, token);
            token.ThrowIfCancellationRequested();

            GridQuotes.ItemsSource = summaries;
            TxtSubtitle.Text = $"{summaries.Count} preventivi trovati. Sono esclusi confermati, finiti, archiviati e rifiutati.";
            TxtEmpty.Visibility = summaries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            TxtSubtitle.Text = "Caricamento non riuscito.";
            MessageBox.Show(
                $"Errore durante il caricamento dei preventivi inviati aperti.\n\n{ex.Message}",
                "Preventivi inviati aperti",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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
}
