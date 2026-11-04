using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class OrderMaterialsPreview : UserControl
{
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _copyCancellation;

    public OrderMaterialsPreview()
    {
        InitializeComponent();
        Loaded += OnLoadMaterials;
        Unloaded += (_, _) =>
        {
            CancelLoad();
            _copyCancellation?.Cancel();
        };
        DataContextChanged += async (_, _) =>
        {
            CancelLoad();
            _copyCancellation?.Cancel();
            CopyMessage.Text = "";
            await LoadMaterialsAsync();
        };
    }

    private async void OnLoadMaterials(object sender, RoutedEventArgs e) => await LoadMaterialsAsync();

    private async Task LoadMaterialsAsync()
    {
        if (!IsLoaded || !MaterialsExpander.IsExpanded || _loadCancellation != null ||
            DataContext is not QuoteHistorySummary order || string.IsNullOrWhiteSpace(order.QuoteNumber))
            return;

        using var cancellation = AppShutdownManager.CreateLinkedTokenSource();
        _loadCancellation = cancellation;
        var token = cancellation.Token;
        MaterialRows.ItemsSource = null;
        LoadMessage.Text = "Caricamento materiali…";
        LoadMessage.Visibility = Visibility.Visible;

        try
        {
            var quote = await App.DataService.GetQuoteByNumberAsync(order.QuoteNumber, token, includeAttachments: false);
            token.ThrowIfCancellationRequested();
            if (quote == null)
            {
                LoadMessage.Text = "Preventivo non trovato.";
                return;
            }

            var materials = SupplierOrderSummaryText.GetMaterials(quote).ToList();
            MaterialRows.ItemsSource = materials;
            if (materials.Count == 0)
                LoadMessage.Text = "Nessun materiale nel preventivo.";
            else
                LoadMessage.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Ordini] Caricamento materiali {order.QuoteNumber}: {ex.Message}");
            if (!token.IsCancellationRequested)
                LoadMessage.Text = "Impossibile caricare i materiali. Chiudi e riapri l’elenco per riprovare.";
        }
        finally
        {
            if (_loadCancellation == cancellation)
                _loadCancellation = null;
        }
    }

    private void OnCancelLoad(object sender, RoutedEventArgs e) => CancelLoad();

    private void CancelLoad()
    {
        var cancellation = _loadCancellation;
        _loadCancellation = null;
        cancellation?.Cancel();
    }

    private async void OnCopySummaryClick(object sender, RoutedEventArgs e)
    {
        if (_copyCancellation != null || DataContext is not QuoteHistorySummary order ||
            string.IsNullOrWhiteSpace(order.QuoteNumber)) return;

        using var cancellation = AppShutdownManager.CreateLinkedTokenSource();
        _copyCancellation = cancellation;
        var token = cancellation.Token;
        CopySummaryButton.IsEnabled = false;
        CopyMessage.Text = "Preparazione del riepilogo…";
        try
        {
            var quote = await App.DataService.GetQuoteByNumberAsync(order.QuoteNumber, token, includeAttachments: false);
            token.ThrowIfCancellationRequested();
            if (quote == null)
            {
                CopyMessage.Text = "Preventivo non trovato.";
                return;
            }

            var customers = await App.DataService.GetCustomersAsync(token);
            token.ThrowIfCancellationRequested();
            Clipboard.SetDataObject(SupplierOrderSummaryText.Format(order, quote, customers), copy: true);
            CopyMessage.Text = "Riepilogo copiato.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Ordini] Copia riepilogo {order.QuoteNumber}: {ex.Message}");
            if (!token.IsCancellationRequested)
                CopyMessage.Text = "Copia non riuscita. Riprova.";
        }
        finally
        {
            if (token.IsCancellationRequested) CopyMessage.Text = "";
            _copyCancellation = null;
            CopySummaryButton.IsEnabled = true;
        }
    }
}
