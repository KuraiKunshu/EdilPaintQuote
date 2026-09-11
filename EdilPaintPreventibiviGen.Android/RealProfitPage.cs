using System.Globalization;
using EdilPaintPreventibiviGen.Android.Controls;
using EdilPaintPreventibiviGen.Android.Models;
using EdilPaintPreventibiviGen.Android.Services;

namespace EdilPaintPreventibiviGen.Android;

public sealed class RealProfitPage : OperationPage
{
    private readonly QuoteDetail _quote;
    private long _revision;
    private readonly Entry _revenue;
    private readonly Entry _discount;
    private readonly Entry _reduction;
    private readonly Entry _workers;
    private readonly Entry _days;
    private readonly Entry _hours;
    private readonly Entry _hourly;
    private readonly Switch _exclude;
    private readonly List<ProfitMaterialCost> _materials;
    private readonly List<(Entry Name, Entry Quantity, Entry Price, string Source)> _costRows = [];
    private readonly VerticalStackLayout _costs = new() { Spacing = 12 };
    private readonly Label _result = Heading("");
    private readonly Label _saved = new() { FontSize = 12, TextColor = Color.FromArgb("#616166") };
    private bool _defaultsLoaded;

    public RealProfitPage(string connectionString, QuoteDetail quote) : base(connectionString, "Guadagno reale")
    {
        _quote = quote;
        _revision = quote.Revision;
        RealProfitInput? saved = quote.RealProfit?.Input;
        _revenue = Input(Format(saved?.QuoteRevenue ?? (quote.MaterialsOrderedByCustomer
            ? quote.Labors.Sum(line => line.Total) * (1 - quote.LaborDiscount / 100) : quote.Imponibile)), true);
        _discount = Input(Format(saved?.SupplierDiscount ?? 0), true);
        _reduction = Input(Format(saved?.ProfitReductionPercentage ?? 0), true);
        _workers = Input(Format(saved?.Workers ?? 2), true);
        _days = Input(Format(saved?.Days ?? 1), true);
        _hours = Input(Format(saved?.HoursPerDay ?? 10), true);
        _hourly = Input(Format(saved?.HourlyCost ?? 40), true);
        _exclude = new Switch { IsToggled = saved?.ExcludeMaterials ?? quote.MaterialsOrderedByCustomer };
        _materials = saved?.Materials ?? quote.Materials.Select(line => new ProfitMaterialCost
        {
            Name = line.Name, Quantity = line.Quantity, CustomerUnitPrice = line.UnitPrice,
            CustomerDiscount = 100 - (1 - line.Discount / 100) * (1 - quote.MaterialDiscount / 100) * 100
        }).ToList();
        Form.Add(Heading(quote.CustomerName));
        Form.Add(_saved);
        Form.Add(Field("Ricavo imponibile", _revenue));
        Form.Add(Toggle("Escludi materiali del cliente", _exclude));
        Form.Add(Field("Sconto fornitore (%)", _discount));
        Form.Add(Action("Applica sconto del fornitore", async () =>
        {
            var suppliers = await Database.GetSuppliersAsync(ConnectionString);
            string? selected = await DisplayActionSheetAsync("Fornitore", "Annulla", null, suppliers.Select(x => x.BusinessName).ToArray());
            SupplierRecord? supplier = suppliers.FirstOrDefault(x => x.BusinessName == selected);
            if (supplier != null) _discount.Text = Format(supplier.SupplierDiscount);
        }, true));
        Form.Add(Heading("Manodopera"));
        Form.Add(Field("Operai", _workers));
        Form.Add(Field("Giorni", _days));
        Form.Add(Field("Ore al giorno", _hours));
        Form.Add(Field("Costo orario", _hourly));
        Form.Add(Heading("Materiali e costi aziendali"));
        Form.Add(Action("Calcola materiali automatici", CalculateAutomaticAsync, true));
        Form.Add(Action("Regole materiali", async () => await Navigation.PushAsync(new AutomaticMaterialSettingsPage(ConnectionString)), true));
        Form.Add(_costs);
        foreach (CompanyMaterialCost cost in saved?.CompanyMaterials ?? []) AddCost(cost);
        Form.Add(Action("Aggiungi costo", () => { AddCost(new()); return Task.CompletedTask; }, true));
        Form.Add(Action("Scegli materiale aziendale", async () =>
        {
            await Navigation.PushAsync(new CatalogPickerPage(ConnectionString, QuoteLineKind.Material, item => AddCost(new()
            { Name = item.Name, Quantity = 1, UnitCost = item.UnitPrice, Source = "Catalogo" }), companyOnly: true));
        }, true));
        Form.Add(Field("Riduzione prudenziale (%)", _reduction));
        Form.Add(_result);
        Form.Add(Action("Ricalcola e salva", async () =>
        {
            RealProfitInput input = BuildInput();
            var snapshot = new RealProfitSnapshot { Input = input, Result = RealProfitCalculator.Calculate(input) };
            _revision = await Database.SaveRealProfitAsync(ConnectionString, _quote.QuoteNumber, snapshot, _revision);
            ShowResult(snapshot.Result);
            _saved.Text = $"Salvato il {snapshot.CalculatedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}";
        }));
        Form.Add(Action("Condividi PDF riservato", async () =>
        {
            if (!await DisplayAlertAsync("Documento riservato", "Il report contiene costi e guadagno interno.", "Continua", "Annulla")) return;
            RealProfitInput input = BuildInput();
            string path = await MobileDocumentService.CreateAsync("Guadagno", MobileDocumentContent.RealProfit(_quote, input));
            await Share.Default.RequestAsync(new ShareFileRequest("Guadagno reale", new ShareFile(path)));
        }, true));
        _saved.Text = quote.RealProfit == null ? "Non ancora salvato" : $"Salvato il {quote.RealProfit.CalculatedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}";
        ShowResult(quote.RealProfit?.Result ?? RealProfitCalculator.Calculate(BuildInput()));
    }

    private void AddCost(CompanyMaterialCost cost)
    {
        var name = Input(cost.Name);
        var quantity = Input(Format(cost.Quantity), true);
        var price = Input(Format(cost.UnitCost), true);
        var row = (name, quantity, price, cost.Source);
        var layout = new VerticalStackLayout { Spacing = 4 };
        layout.Add(Field("Materiale / costo", name));
        var numbers = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 8 };
        numbers.Add(Field("Quantita", quantity), 0);
        numbers.Add(Field("Costo unitario", price), 1);
        layout.Add(numbers);
        layout.Add(Action("Rimuovi costo", () => { _costRows.Remove(row); _costs.Remove(layout); return Task.CompletedTask; }, true));
        _costRows.Add(row);
        _costs.Add(layout);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_defaultsLoaded || _quote.RealProfit != null) return;
        _defaultsLoaded = true;
        await RunAsync(async () =>
        {
            var settings = await MobileSettings.LoadAsync();
            _workers.Text = Format(settings.Workers);
            _days.Text = Format(settings.Days);
            _hours.Text = Format(settings.HoursPerDay);
            _hourly.Text = Format(settings.HourlyCost);
            _reduction.Text = Format(settings.ProfitReductionPercentage);
            ShowResult(RealProfitCalculator.Calculate(BuildInput()));
        });
    }

    private async Task CalculateAutomaticAsync()
    {
        var settings = await MobileSettings.LoadAsync();
        var catalog = await Database.GetCatalogAsync(ConnectionString, QuoteLineKind.Material, "");
        var currentCosts = BuildInput().CompanyMaterials;
        var input = new EdilPaintPreventibiviGen.Models.AutomaticWindowMaterialCalculationInput
        {
            WindowPrefixes = settings.WindowPrefixes,
            Rules = settings.MaterialRules,
            WindowProducts = _quote.Materials.Select(x => new EdilPaintPreventibiviGen.Models.AutomaticWindowProductLine(x.Name, x.Quantity)).ToArray(),
            Labors = _quote.Labors.Select(x => new EdilPaintPreventibiviGen.Models.AutomaticWindowLaborLine(x.CatalogItemId, x.Name, x.Quantity)).ToArray(),
            MaterialCatalog = catalog.Select(x => new EdilPaintPreventibiviGen.Models.AutomaticMaterialCatalogItem(x.Id, x.Name)).ToArray(),
            ExistingQuoteMaterials = _quote.Materials.Select(x => new EdilPaintPreventibiviGen.Models.AutomaticQuoteMaterialLine(x.CatalogItemId, x.Name, x.Quantity))
                .Concat(currentCosts.Select(x => new EdilPaintPreventibiviGen.Models.AutomaticQuoteMaterialLine(0, x.Name, x.Quantity))).ToArray()
        };
        var result = EdilPaintPreventibiviGen.Services.AutomaticWindowMaterialCalculator.Calculate(input);
        var additions = result.Materials.Where(x => x.QuantityToAdd > 0 && x.QuantityToAdd <= int.MaxValue && catalog.Any(c => c.Id == x.MaterialCatalogItemId)).ToList();
        string preview = string.Join("\n", additions.Select(x => $"N.{x.QuantityToAdd} {x.MaterialName}"));
        string issues = string.Join("\n", result.Issues.Select(x => x.Message));
        if (additions.Count == 0) { await DisplayAlertAsync("Materiali automatici", "Nessun materiale da aggiungere.\n" + issues, "OK"); return; }
        if (!await DisplayAlertAsync("Materiali automatici", preview + "\n\n" + issues, "Aggiungi", "Annulla")) return;
        foreach (var item in additions)
        {
            var product = catalog.First(x => x.Id == item.MaterialCatalogItemId);
            AddCost(new CompanyMaterialCost { Name = product.Name, Quantity = (int)item.QuantityToAdd, UnitCost = product.UnitPrice, Source = "Automatico" });
        }
        _saved.Text = "Costi modificati, da ricalcolare e salvare";
    }

    private RealProfitInput BuildInput() => new()
    {
        QuoteRevenue = Number(_revenue, "Ricavo"), ExcludeMaterials = _exclude.IsToggled,
        SupplierDiscount = Number(_discount, "Sconto", 100), ProfitReductionPercentage = Number(_reduction, "Riduzione", 100),
        Workers = WholeNumber(_workers, "Operai"), Days = Number(_days, "Giorni"),
        HoursPerDay = Number(_hours, "Ore al giorno", 24), HourlyCost = Number(_hourly, "Costo orario"),
        Materials = _materials,
        CompanyMaterials = _costRows.Select(row => new CompanyMaterialCost
        {
            Name = string.IsNullOrWhiteSpace(row.Name.Text) ? throw new InvalidOperationException("Inserisci il nome dei costi aziendali.") : row.Name.Text.Trim(),
            Quantity = WholeNumber(row.Quantity, "Quantita"), UnitCost = Number(row.Price, "Costo"), Source = row.Source
        }).ToList()
    };

    private void ShowResult(RealProfitResult result)
    {
        var culture = CultureInfo.GetCultureInfo("it-IT");
        _result.Text = $"Costi totali: {result.TotalCosts.ToString("C", culture)}\nGuadagno: {result.Profit.ToString("C", culture)} ({result.ProfitPercentage:0.#}%)";
        _result.TextColor = Color.FromArgb(result.Profit >= 0 ? "#166534" : "#B3261E");
    }
}
