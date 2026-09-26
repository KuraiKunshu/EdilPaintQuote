using QuantityValue = EdilPaintPreventibiviGen.Models.QuantityValue;
using System.Globalization;
using EdilPaintPreventibiviGen.Android.Models;

namespace EdilPaintPreventibiviGen.Android;

public partial class QuoteLineEditorPage : ContentPage
{
    private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");
    private readonly string _connectionString;
    private readonly QuoteLineKind _kind;
    private readonly Action<QuoteLine> _onSaved;
    private readonly QuoteLine _line;

    public QuoteLineEditorPage(
        string connectionString,
        QuoteLineKind kind,
        Action<QuoteLine> onSaved,
        QuoteLine? line = null)
    {
        InitializeComponent();
        _connectionString = connectionString;
        _kind = kind;
        _onSaved = onSaved;
        _line = line?.Clone() ?? new QuoteLine();

        string noun = kind == QuoteLineKind.Material ? "materiale" : "lavorazione";
        HeaderTitleLabel.Text = line == null ? $"Nuovo {noun}" : $"Modifica {noun}";
        Title = HeaderTitleLabel.Text;
        SignificantLabel.Text = kind == QuoteLineKind.Material
            ? "Bene significativo"
            : "Lavorazione collegata a beni significativi";

        NameEntry.Text = _line.Name;
        DescriptionEditor.Text = _line.Description;
        UnitPriceEntry.Text = _line.UnitPrice.ToString("0.##", ItalianCulture);
        QuantityEntry.Text = QuantityValue.Format(_line.Quantity);
        UnitPicker.ItemsSource = QuantityValue.Units.ToList();
        UnitPicker.SelectedItem = _line.UnitOfMeasure;
        DiscountEntry.Text = _line.Discount.ToString("0.##", ItalianCulture);
        SignificantCheckBox.IsChecked = _line.IsSignificant;
        UpdateTotalPreview();
    }

    private async void OnCatalogClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new CatalogPickerPage(
            _connectionString,
            _kind,
            ApplyCatalogItem));
    }

    private void ApplyCatalogItem(CatalogItem item)
    {
        _line.CatalogItemId = item.Id;
        NameEntry.Text = item.Name;
        UnitPicker.SelectedItem = item.UnitOfMeasure;
        DescriptionEditor.Text = item.Description;
        UnitPriceEntry.Text = item.UnitPrice.ToString("0.##", ItalianCulture);
        SignificantCheckBox.IsChecked = item.IsSignificant;
        UpdateTotalPreview();
    }

    private void OnValueChanged(object? sender, TextChangedEventArgs e) => UpdateTotalPreview();

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        string name = NameEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlertAsync("Voce", "Inserisci il nome della voce.", "OK");
            NameEntry.Focus();
            return;
        }

        if (!TryParseNumber(UnitPriceEntry.Text, out double unitPrice) || unitPrice < 0 ||
            !QuantityValue.TryParse(QuantityEntry.Text, out decimal quantity) ||
            !TryParseNumber(DiscountEntry.Text, out double discount) || discount is < 0 or > 100)
        {
            await DisplayAlertAsync(
                "Voce",
                "Controlla prezzo, quantità e sconto. La quantità deve essere positiva, con al massimo 9 decimali e lo sconto compreso tra 0 e 100.",
                "OK");
            return;
        }

        _line.Name = name;
        _line.Description = DescriptionEditor.Text?.Trim() ?? string.Empty;
        _line.UnitPrice = unitPrice;
        _line.Quantity = quantity;
        _line.UnitOfMeasure = UnitPicker.SelectedItem as string ?? "pz";
        _line.Discount = discount;
        _line.IsSignificant = SignificantCheckBox.IsChecked;
        _onSaved(_line.Clone());
        await Navigation.PopAsync();
    }

    private void UpdateTotalPreview()
    {
        double unitPrice = TryParseNumber(UnitPriceEntry.Text, out double parsedPrice) ? parsedPrice : 0;
        decimal quantity = QuantityValue.TryParse(QuantityEntry.Text, out decimal parsedQuantity)
            ? parsedQuantity
            : 0;
        double discount = TryParseNumber(DiscountEntry.Text, out double parsedDiscount) ? parsedDiscount : 0;
        double total = Math.Max(0, unitPrice) * (double)Math.Max(0, quantity) * (1 - Math.Clamp(discount, 0, 100) / 100);
        TotalLabel.Text = total.ToString("C", ItalianCulture);
    }

    private static bool TryParseNumber(string? text, out double value) =>
        MobileNumber.TryParse(text, out value);
}
