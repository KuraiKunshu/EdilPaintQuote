using System.Collections.ObjectModel;
using EdilPaintPreventibiviGen.Android.Models;
using EdilPaintPreventibiviGen.Android.Services;

namespace EdilPaintPreventibiviGen.Android;

public partial class CatalogPickerPage : ContentPage
{
    private readonly string _connectionString;
    private readonly QuoteLineKind _kind;
    private readonly Action<CatalogItem> _onSelected;
    private readonly MobileDatabaseService _databaseService = new();
    private readonly ObservableCollection<CatalogItem> _items = [];
    private CancellationTokenSource? _searchCts;
    private bool _loaded;
    private readonly bool _companyOnly;
    private int _loadVersion;

    public CatalogPickerPage(
        string connectionString,
        QuoteLineKind kind,
        Action<CatalogItem> onSelected,
        bool companyOnly = false)
    {
        InitializeComponent();
        _connectionString = connectionString;
        _kind = kind;
        _onSelected = onSelected;
        _companyOnly = companyOnly;
        Title = kind == QuoteLineKind.Material ? "Catalogo materiali" : "Catalogo lavorazioni";
        CatalogList.ItemsSource = _items;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded)
            return;
        _loaded = true;
        await LoadAsync();
    }

    private async void OnSearchRequested(object? sender, EventArgs e) => await LoadAsync();

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        CancellationToken token = _searchCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token);
                await MainThread.InvokeOnMainThreadAsync(LoadAsync);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private async void OnItemSelected(object? sender, SelectionChangedEventArgs e)
    {
        var item = e.CurrentSelection.FirstOrDefault() as CatalogItem;
        CatalogList.SelectedItem = null;
        if (item == null)
            return;
        _onSelected(item);
        await Navigation.PopAsync();
    }

    private async Task LoadAsync()
    {
        int version = ++_loadVersion;
        try
        {
            SetBusy(true);
            IReadOnlyList<CatalogItem> items = await _databaseService.GetCatalogAsync(
                _connectionString,
                _kind,
                SearchBox.Text?.Trim() ?? string.Empty);
            if (version != _loadVersion) return;
            _items.Clear();
            foreach (var item in items.Where(item => !_companyOnly || item.IsCompanyMaterial))
                _items.Add(item);
            CountLabel.Text = _items.Count == 1 ? "1 voce" : $"{_items.Count} voci";
        }
        catch (Exception exception)
        {
            if (version != _loadVersion) return;
            await DisplayAlertAsync(
                "Catalogo non disponibile",
                MobileDatabaseService.GetUserMessage(exception),
                "OK");
        }
        finally
        {
            if (version == _loadVersion) SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy)
    {
        Busy.IsVisible = isBusy;
        Busy.IsRunning = isBusy;
    }
}
