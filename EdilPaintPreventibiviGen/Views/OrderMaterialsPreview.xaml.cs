using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class OrderMaterialsPreview : UserControl
{
    private CancellationTokenSource? _loadCancellation;

    public OrderMaterialsPreview()
    {
        InitializeComponent();
        Loaded += OnLoadMaterials;
        Unloaded += OnCancelLoad;
        DataContextChanged += async (_, _) =>
        {
            CancelLoad();
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

            var materials = quote.Materials
                .Where(material => !string.IsNullOrWhiteSpace(material.Name))
                .OrderBy(material => material.SortOrder)
                .ToList();
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
}
