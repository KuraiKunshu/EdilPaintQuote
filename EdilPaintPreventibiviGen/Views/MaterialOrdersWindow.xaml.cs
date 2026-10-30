using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class MaterialOrdersWindow : Window
{
    private readonly IDataService _data;
    private List<QuoteHistorySummary> _quotes = [];
    private readonly CancellationTokenSource _lifetime = AppShutdownManager.CreateLinkedTokenSource();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _loading, _updating, _closed;
    private string _sourceStatus = "In attesa del database.";

    public MaterialOrdersWindow(IDataService data)
    {
        _data = data;
        InitializeComponent();
        CmbSupplier.ItemsSource = new[] { "Tutti i fornitori" };
        CmbSupplier.SelectedIndex = 0;
        Loaded += async (_, _) => { await RefreshAsync(); if (!_closed) _timer.Start(); };
        _timer.Tick += async (_, _) =>
        {
            if (IsVisible && !TxtSearch.IsKeyboardFocusWithin && !CmbSupplier.IsDropDownOpen &&
                !DpFrom.IsKeyboardFocusWithin && !DpTo.IsKeyboardFocusWithin && !DpFrom.IsDropDownOpen && !DpTo.IsDropDownOpen)
                await RefreshAsync();
        };
        Closed += (_, _) => { _closed = true; _timer.Stop(); _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    private async Task RefreshAsync()
    {
        if (_loading || _closed) return;
        _loading = true; BtnRefresh.IsEnabled = false;
        try
        {
            var quotes = await _data.GetQuoteSummariesAsync(int.MaxValue, _lifetime.Token);
            if (_closed) return;
            _quotes = quotes.Where(MaterialOrderRegister.IsRecorded).ToList();
            string? selected = CmbSupplier.SelectedItem as string;
            _updating = true;
            try
            {
                var suppliers = _quotes.Select(quote => quote.OrderSupplierGroup).Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase).Prepend("Tutti i fornitori").ToList();
                if (selected != null && !suppliers.Contains(selected)) suppliers.Add(selected);
                CmbSupplier.ItemsSource = suppliers;
                CmbSupplier.SelectedItem = selected ?? "Tutti i fornitori";
            }
            finally { _updating = false; }
            _sourceStatus = $"Dati aggiornati dal database alle {DateTime.Now:HH:mm:ss}.";
            ApplyFilters();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { if (!_closed) TxtStatus.Text = _sourceStatus = $"Aggiornamento non riuscito. {ex.Message} I dati precedentemente caricati restano visibili."; }
        finally { _loading = false; if (!_closed) BtnRefresh.IsEnabled = true; }
    }

    private void ApplyFilters()
    {
        if (_updating || CmbSupplier == null || DpFrom == null || DpTo == null) return;
        try
        {
            string? supplier = CmbSupplier.SelectedIndex > 0 ? CmbSupplier.SelectedItem as string : null;
            var rows = MaterialOrderRegister.Filter(_quotes, TxtSearch.Text, supplier, DpFrom.SelectedDate, DpTo.SelectedDate);
            GridOrders.ItemsSource = rows;
            TxtCount.Text = $"{rows.Count} ordini · {rows.Count(row => !row.OrderDate.HasValue)} senza data ordine";
            TxtStatus.Text = _sourceStatus;
        }
        catch (ArgumentException ex) { TxtStatus.Text = ex.Message; }
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => ApplyFilters();
    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void OnClearFiltersClick(object sender, RoutedEventArgs e)
    {
        _updating = true;
        try { TxtSearch.Clear(); CmbSupplier.SelectedIndex = 0; DpFrom.SelectedDate = DpTo.SelectedDate = null; }
        finally { _updating = false; }
        ApplyFilters();
    }
    private void OnDateError(object? sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        TxtStatus.Text = "Inserisci una data valida nel formato gg/mm/aaaa.";
    }
}
