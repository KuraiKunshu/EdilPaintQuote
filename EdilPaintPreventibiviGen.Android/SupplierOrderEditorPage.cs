using EdilPaintPreventibiviGen.Android.Controls;
using EdilPaintPreventibiviGen.Android.Models;

namespace EdilPaintPreventibiviGen.Android;

public sealed class SupplierOrderEditorPage : OperationPage
{
    private readonly string _quoteNumber;
    private QuoteDetail? _quote;
    private readonly Entry _supplier = Input();
    private readonly Picker _suppliers = new() { Title = "Seleziona fornitore" };
    private readonly Switch _customerOrder = new();
    private readonly Switch _hasDate = new();
    private readonly Switch _hasDelivery = new();
    private readonly DatePicker _date = new() { Format = "dd/MM/yyyy" };
    private readonly DatePicker _delivery = new() { Format = "dd/MM/yyyy" };
    private readonly Picker _status = new() { ItemsSource = SupplierOrderStatusOptions.All.ToList() };
    private readonly Label _title = Heading("");
    private bool _loaded;

    public SupplierOrderEditorPage(string connectionString, SupplierOrderSummary order)
        : this(connectionString, order.QuoteNumber) { }

    public SupplierOrderEditorPage(string connectionString, string quoteNumber) : base(connectionString, "Ordine materiali")
    {
        _quoteNumber = quoteNumber;
        Form.Add(_title);
        Form.Add(Toggle("Materiali ordinati dal cliente", _customerOrder));
        Form.Add(Field("Fornitore", _suppliers));
        Form.Add(Field("Nome fornitore", _supplier));
        Form.Add(Toggle("Data ordine presente", _hasDate));
        Form.Add(_date);
        Form.Add(Toggle("Consegna prevista presente", _hasDelivery));
        Form.Add(_delivery);
        Form.Add(Field("Stato", _status));
        _suppliers.SelectedIndexChanged += (_, _) =>
        {
            if (_suppliers.SelectedItem is SupplierRecord supplier) _supplier.Text = supplier.BusinessName;
        };
        _customerOrder.Toggled += (_, _) =>
        {
            _supplier.IsEnabled = _suppliers.IsEnabled = !_customerOrder.IsToggled;
            if (_customerOrder.IsToggled) _supplier.Text = _quote?.CustomerName ?? "";
            else if (_supplier.Text == _quote?.CustomerName) _supplier.Text = "";
        };
        _hasDate.Toggled += (_, _) => _date.IsVisible = _hasDate.IsToggled;
        _hasDelivery.Toggled += (_, _) => _delivery.IsVisible = _hasDelivery.IsToggled;
        Form.Add(Action("Salva ordine", async () => { await SaveAsync(); await Navigation.PopAsync(); }));
        Form.Add(Action("Prepara email", async () =>
        {
            if (_customerOrder.IsToggled) throw new InvalidOperationException("L'ordine di questi materiali e' a carico del cliente.");
            await SaveAsync();
            await Navigation.PushAsync(new MailComposerPage(ConnectionString, _quote!, true));
        }, true));
        Form.Add(Action("Apri preventivo", async () =>
        {
            await Navigation.PushAsync(new QuoteDetailPage(ConnectionString, await Database.GetQuoteAsync(ConnectionString, _quoteNumber)));
        }, true));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        await RunAsync(async () =>
        {
            _quote = await Database.GetQuoteAsync(ConnectionString, _quoteNumber);
            _title.Text = $"{_quote.CustomerName}\nPreventivo {_quote.QuoteNumber}";
            _suppliers.ItemsSource = (await Database.GetSuppliersAsync(ConnectionString)).ToList();
            _supplier.Text = _quote.SupplierName;
            _customerOrder.IsToggled = _quote.MaterialsOrderedByCustomer;
            _hasDate.IsToggled = _quote.MaterialOrderDate.HasValue;
            _date.Date = _quote.MaterialOrderDate?.ToLocalTime().Date ?? DateTime.Today;
            _date.IsVisible = _hasDate.IsToggled;
            _hasDelivery.IsToggled = _quote.ExpectedDeliveryDate.HasValue;
            _delivery.Date = _quote.ExpectedDeliveryDate?.ToLocalTime().Date ?? DateTime.Today;
            _delivery.IsVisible = _hasDelivery.IsToggled;
            _status.SelectedItem = SupplierOrderStatusOptions.Normalize(_quote.MaterialStatus);
            _loaded = true;
        });
    }

    private async Task SaveAsync()
    {
        if (!_loaded || _quote == null) throw new InvalidOperationException("Caricamento non completato. Riapri l'ordine prima di salvarlo.");
        await Database.UpdateSupplierOrderAsync(ConnectionString, new SupplierOrderUpdate
        {
            QuoteNumber = _quoteNumber, ExpectedRevision = _quote.Revision,
            SupplierName = _customerOrder.IsToggled ? _quote.CustomerName : _supplier.Text ?? "",
            MaterialsOrderedByCustomer = _customerOrder.IsToggled,
            MaterialOrderDate = _hasDate.IsToggled ? _date.Date : null,
            ExpectedDeliveryDate = _hasDelivery.IsToggled ? _delivery.Date : null,
            MaterialStatus = _status.SelectedItem as string ?? "DA ORDINARE"
        });
        _quote = await Database.GetQuoteAsync(ConnectionString, _quoteNumber);
    }
}
