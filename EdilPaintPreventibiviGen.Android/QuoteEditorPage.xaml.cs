using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using EdilPaintPreventibiviGen.Android.Models;
using EdilPaintPreventibiviGen.Android.Services;

namespace EdilPaintPreventibiviGen.Android;

public partial class QuoteEditorPage : ContentPage
{
    private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");
    private static readonly IReadOnlyList<string> IvaOptions = ["Esclusa", "10%", "22%", "RC 10%+22%"];

    private readonly string _connectionString;
    private readonly MobileDatabaseService _databaseService = new();
    private readonly QuoteDraft _draft;
    private readonly ObservableCollection<CustomerRecord> _customers = [];
    private List<CustomerOption> _optionalCustomerOptions = [];
    private bool _initialized;
    private bool _initializingControls;
    private bool _isSaving;

    public QuoteEditorPage(string connectionString, QuoteDetail? detail = null)
    {
        InitializeComponent();
        _connectionString = connectionString;
        _draft = detail == null ? new QuoteDraft() : QuoteDraft.FromDetail(detail);

        HeaderTitleLabel.Text = _draft.IsNew ? "Nuovo preventivo" : "Modifica preventivo";
        QuoteNumberLabel.Text = _draft.QuoteNumberDisplay;
        SaveButton.Text = _draft.IsNew ? "Crea preventivo" : "Salva modifiche";
        ModeLabel.Text = _draft.Status.ToString().ToUpperInvariant();

        BindableLayout.SetItemsSource(MaterialsList, _draft.Materials);
        BindableLayout.SetItemsSource(LaborsList, _draft.Labors);
        _draft.Materials.CollectionChanged += OnLinesChanged;
        _draft.Labors.CollectionChanged += OnLinesChanged;

        IvaPicker.ItemsSource = IvaOptions.ToList();
        QuoteStatusPicker.ItemsSource = QuoteStatusOptions.Editable.ToList();
        RefreshLineState();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized)
            return;
        _initialized = true;
        await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            SetBusy(true);
            Task<IReadOnlyList<CustomerRecord>> customersTask = _databaseService.GetCustomersAsync(_connectionString);
            Task<QuoteEditorDefaults>? defaultsTask = _draft.IsNew
                ? _databaseService.GetQuoteEditorDefaultsAsync(_connectionString)
                : null;

            IReadOnlyList<CustomerRecord> customers = await customersTask;
            _customers.Clear();
            foreach (var customer in customers)
                _customers.Add(customer);
            EnsureExistingSelectionsAreVisible();

            if (defaultsTask != null)
            {
                QuoteEditorDefaults defaults = await defaultsTask;
                _draft.PaymentTerms = defaults.PaymentTerms;
            }

            PopulateControls();
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(
                "Editor non disponibile",
                MobileDatabaseService.GetUserMessage(exception),
                "OK");
            await Navigation.PopAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void PopulateControls()
    {
        _initializingControls = true;
        try
        {
            CustomerPicker.ItemsSource = _customers;
            CustomerPicker.SelectedItem = FindCustomer(_draft.CustomerId, _draft.CustomerSyncId, _draft.CustomerName);

            _optionalCustomerOptions = [new CustomerOption("Nessuno")];
            _optionalCustomerOptions.AddRange(_customers.Select(customer => new CustomerOption(customer.BusinessName, customer)));
            ReferencePicker.ItemsSource = _optionalCustomerOptions;
            BillingCustomerPicker.ItemsSource = _optionalCustomerOptions;
            ReferencePicker.SelectedItem = FindOptionalCustomer(
                _draft.ReferenceCustomerId,
                _draft.ReferenceCustomerSyncId,
                _draft.ReferenceName);
            BillingCustomerPicker.SelectedItem = FindOptionalCustomer(
                _draft.BillingCustomerId,
                _draft.BillingCustomerSyncId,
                _draft.BillingCustomerName);

            SiteNameEntry.Text = _draft.SiteName;
            QuoteDatePicker.Date = _draft.Date == default ? DateTime.Today : _draft.Date;
            QuoteStatusPicker.SelectedItem = QuoteStatusOptions.Editable.FirstOrDefault(option => option.Value == _draft.Status)
                                                   ?? QuoteStatusOptions.Editable.First();
            MaterialDiscountEntry.Text = _draft.MaterialDiscount.ToString("0.##", ItalianCulture);
            LaborDiscountEntry.Text = _draft.LaborDiscount.ToString("0.##", ItalianCulture);
            IvaPicker.SelectedItem = DisplayIva(_draft.IvaType);
            PaymentTermsEditor.Text = _draft.PaymentTerms;
            CustomerNotesEditor.Text = _draft.CustomerNotes;
            InternalNotesEditor.Text = _draft.Notes;
            UpdateTotals();
        }
        finally
        {
            _initializingControls = false;
        }
    }

    private void EnsureExistingSelectionsAreVisible()
    {
        AddMissingCustomer(_draft.CustomerId, _draft.CustomerSyncId, _draft.CustomerName);
        AddMissingCustomer(_draft.ReferenceCustomerId, _draft.ReferenceCustomerSyncId, _draft.ReferenceName);
        AddMissingCustomer(_draft.BillingCustomerId, _draft.BillingCustomerSyncId, _draft.BillingCustomerName);
    }

    private void AddMissingCustomer(int? id, Guid syncId, string name)
    {
        if (!id.HasValue || string.IsNullOrWhiteSpace(name) || FindCustomer(id, syncId, name) != null)
            return;
        _customers.Add(new CustomerRecord { Id = id.Value, SyncId = syncId, BusinessName = name });
    }

    private CustomerRecord? FindCustomer(int? id, Guid syncId, string name)
    {
        if (syncId != Guid.Empty)
        {
            CustomerRecord? bySyncId = _customers.FirstOrDefault(customer => customer.SyncId == syncId);
            if (bySyncId != null)
                return bySyncId;
        }

        if (id.HasValue)
        {
            CustomerRecord? byId = _customers.FirstOrDefault(customer => customer.Id == id.Value);
            if (byId != null)
                return byId;
        }

        return _customers.FirstOrDefault(customer =>
            customer.BusinessName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private CustomerOption FindOptionalCustomer(int? id, Guid syncId, string name)
    {
        CustomerRecord? customer = FindCustomer(id, syncId, name);
        return customer == null
            ? _optionalCustomerOptions[0]
            : _optionalCustomerOptions.First(option => option.Customer?.Id == customer.Id);
    }

    private void OnCustomerChanged(object? sender, EventArgs e)
    {
        if (_initializingControls || CustomerPicker.SelectedItem is not CustomerRecord customer)
            return;
        MaterialDiscountEntry.Text = customer.MaterialDiscount.ToString("0.##", ItalianCulture);
        LaborDiscountEntry.Text = customer.LaborDiscount.ToString("0.##", ItalianCulture);
        UpdateTotals();
    }

    private async void OnCreateCustomerClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new CustomerEditorPage(
            _connectionString,
            onSaved: AddAndSelectCustomer));
    }

    private void AddAndSelectCustomer(CustomerRecord saved)
    {
        CustomerRecord? previousReference = (ReferencePicker.SelectedItem as CustomerOption)?.Customer;
        CustomerRecord? previousBillingCustomer = (BillingCustomerPicker.SelectedItem as CustomerOption)?.Customer;

        CustomerRecord? existing = _customers.FirstOrDefault(customer => customer.SyncId == saved.SyncId);
        if (existing != null)
            _customers.Remove(existing);
        _customers.Add(saved);
        var ordered = _customers.OrderBy(customer => customer.BusinessName, StringComparer.CurrentCultureIgnoreCase).ToList();
        _customers.Clear();
        foreach (var customer in ordered)
            _customers.Add(customer);

        _initializingControls = true;
        CustomerPicker.ItemsSource = null;
        CustomerPicker.ItemsSource = _customers;
        CustomerPicker.SelectedItem = _customers.First(customer => customer.SyncId == saved.SyncId);
        _optionalCustomerOptions = [new CustomerOption("Nessuno")];
        _optionalCustomerOptions.AddRange(_customers.Select(customer => new CustomerOption(customer.BusinessName, customer)));
        ReferencePicker.ItemsSource = _optionalCustomerOptions;
        BillingCustomerPicker.ItemsSource = _optionalCustomerOptions;
        ReferencePicker.SelectedItem = previousReference == null
            ? _optionalCustomerOptions[0]
            : FindOptionalCustomer(previousReference.Id, previousReference.SyncId, previousReference.BusinessName);
        BillingCustomerPicker.SelectedItem = previousBillingCustomer == null
            ? _optionalCustomerOptions[0]
            : FindOptionalCustomer(
                previousBillingCustomer.Id,
                previousBillingCustomer.SyncId,
                previousBillingCustomer.BusinessName);
        _initializingControls = false;
        MaterialDiscountEntry.Text = saved.MaterialDiscount.ToString("0.##", ItalianCulture);
        LaborDiscountEntry.Text = saved.LaborDiscount.ToString("0.##", ItalianCulture);
        UpdateTotals();
    }

    private async void OnAddMaterialClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new QuoteLineEditorPage(
            _connectionString,
            QuoteLineKind.Material,
            line => _draft.Materials.Add(line)));

    private async void OnAddLaborClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new QuoteLineEditorPage(
            _connectionString,
            QuoteLineKind.Labor,
            line => _draft.Labors.Add(line)));

    private async void OnEditMaterialClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is QuoteLine line)
            await EditLineAsync(_draft.Materials, line, QuoteLineKind.Material);
    }

    private async void OnEditLaborClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is QuoteLine line)
            await EditLineAsync(_draft.Labors, line, QuoteLineKind.Labor);
    }

    private async Task EditLineAsync(
        ObservableCollection<QuoteLine> collection,
        QuoteLine original,
        QuoteLineKind kind)
    {
        int index = collection.IndexOf(original);
        if (index < 0)
            return;
        await Navigation.PushAsync(new QuoteLineEditorPage(
            _connectionString,
            kind,
            updated => collection[index] = updated,
            original));
    }

    private async void OnRemoveMaterialClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is QuoteLine line)
            await RemoveLineAsync(_draft.Materials, line);
    }

    private async void OnRemoveLaborClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is QuoteLine line)
            await RemoveLineAsync(_draft.Labors, line);
    }

    private async Task RemoveLineAsync(ObservableCollection<QuoteLine> collection, QuoteLine line)
    {
        bool confirm = await DisplayAlertAsync("Rimuovi voce", line.Name, "Rimuovi", "Annulla");
        if (confirm)
            collection.Remove(line);
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshLineState();
        UpdateTotals();
    }

    private void RefreshLineState()
    {
        NoMaterialsLabel.IsVisible = _draft.Materials.Count == 0;
        NoLaborsLabel.IsVisible = _draft.Labors.Count == 0;
    }

    private void OnTotalsInputChanged(object? sender, EventArgs e)
    {
        if (!_initializingControls)
            UpdateTotals();
    }

    private void UpdateTotals()
    {
        double materialDiscount = ParseNumberOrZero(MaterialDiscountEntry.Text);
        double laborDiscount = ParseNumberOrZero(LaborDiscountEntry.Text);
        string ivaType = IvaPicker.SelectedItem as string ?? "Esclusa";
        QuoteTotals totals = QuoteTotalsCalculator.Calculate(
            _draft.Materials,
            _draft.Labors,
            materialDiscount,
            laborDiscount,
            ivaType);
        TaxableLabel.Text = totals.Imponibile.ToString("C", ItalianCulture);
        VatLabel.Text = totals.Iva.ToString("C", ItalianCulture);
        GrandTotalLabel.Text = totals.Total.ToString("C", ItalianCulture);
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_isSaving)
            return;
        if (CustomerPicker.SelectedItem is not CustomerRecord customer)
        {
            await DisplayAlertAsync("Preventivo", "Seleziona il cliente.", "OK");
            return;
        }

        if (!TryParsePercentage(MaterialDiscountEntry.Text, out double materialDiscount) ||
            !TryParsePercentage(LaborDiscountEntry.Text, out double laborDiscount))
        {
            await DisplayAlertAsync("Preventivo", "Gli sconti devono essere compresi tra 0 e 100.", "OK");
            return;
        }

        var reference = (ReferencePicker.SelectedItem as CustomerOption)?.Customer;
        var billing = (BillingCustomerPicker.SelectedItem as CustomerOption)?.Customer;
        var status = QuoteStatusPicker.SelectedItem as QuoteStatusOption;

        _draft.Date = QuoteDatePicker.Date ?? DateTime.Today;
        _draft.CustomerId = customer.Id;
        _draft.CustomerSyncId = customer.SyncId;
        _draft.CustomerName = customer.BusinessName;
        _draft.ReferenceCustomerId = reference?.Id;
        _draft.ReferenceCustomerSyncId = reference?.SyncId ?? Guid.Empty;
        _draft.ReferenceName = reference?.BusinessName ?? string.Empty;
        _draft.BillingCustomerId = billing?.Id;
        _draft.BillingCustomerSyncId = billing?.SyncId ?? Guid.Empty;
        _draft.BillingCustomerName = billing?.BusinessName ?? string.Empty;
        _draft.SiteName = SiteNameEntry.Text?.Trim() ?? string.Empty;
        _draft.Status = status?.Value ?? QuoteStatus.Bozza;
        _draft.MaterialDiscount = materialDiscount;
        _draft.LaborDiscount = laborDiscount;
        _draft.IvaType = IvaPicker.SelectedItem as string ?? "Esclusa";
        _draft.PaymentTerms = PaymentTermsEditor.Text?.Trim() ?? string.Empty;
        _draft.CustomerNotes = CustomerNotesEditor.Text?.Trim() ?? string.Empty;
        _draft.Notes = InternalNotesEditor.Text?.Trim() ?? string.Empty;

        try
        {
            SetBusy(true);
            QuoteSaveResult result = await _databaseService.SaveQuoteAsync(_connectionString, _draft);
            await DisplayAlertAsync(
                "Preventivo salvato",
                $"Preventivo {result.QuoteNumber}",
                "OK");
            await Navigation.PopToRootAsync();
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(
                "Salvataggio non riuscito",
                MobileDatabaseService.GetUserMessage(exception),
                "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy)
    {
        _isSaving = isBusy;
        Busy.IsVisible = isBusy;
        Busy.IsRunning = isBusy;
        SaveButton.IsEnabled = !isBusy;
    }

    private static bool TryParsePercentage(string? text, out double value)
    {
        bool parsed = TryParseNumber(text, out value);
        return parsed && value is >= 0 and <= 100;
    }

    private static double ParseNumberOrZero(string? text) =>
        TryParseNumber(text, out double value) ? value : 0;

    private static bool TryParseNumber(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Number, ItalianCulture, out value) ||
        double.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static string DisplayIva(string? value) => QuoteTotalsCalculator.NormalizeIvaType(value) switch
    {
        "10%" => "10%",
        "22%" => "22%",
        "RC 10%+22%" => "RC 10%+22%",
        _ => "Esclusa"
    };
}
