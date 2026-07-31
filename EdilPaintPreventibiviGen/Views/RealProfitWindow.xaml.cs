using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class RealProfitWindow : Window
{
    private readonly QuoteHistoryEntry _quote;
    private readonly ObservableCollection<ProfitMaterialCost> _materials;
    private readonly ObservableCollection<CompanyMaterialCost> _companyMaterials = [];
    private readonly List<Item> _availableCompanyMaterials;

    private readonly bool _excludeMaterials;

    public RealProfitWindow(
        QuoteHistoryEntry quote,
        double supplierDiscount,
        bool customerIsSupplier,
        IEnumerable<Item> companyMaterials)
    {
        InitializeComponent();
        _quote = quote;
        _excludeMaterials = customerIsSupplier;
        _availableCompanyMaterials = companyMaterials
            .Where(material => material.IsCompanyMaterial)
            .OrderBy(material => material.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _materials = new ObservableCollection<ProfitMaterialCost>(quote.Materials.Select(material =>
            new ProfitMaterialCost
            {
                Name = material.Name,
                Quantity = material.Quantity,
                CustomerUnitPrice = material.UnitPrice,
                CustomerDiscount = 100 -
                    (1 - Math.Clamp(material.Discount, 0, 100) / 100) *
                    (1 - Math.Clamp(quote.MaterialDiscount, 0, 100) / 100) *
                    100
            }));

        GridMaterialCosts.ItemsSource = _materials;
        CboCompanyMaterialSearch.ItemsSource = _availableCompanyMaterials;
        GridCompanyMaterials.ItemsSource = _companyMaterials;
        TxtQuoteInfo.Text = customerIsSupplier
            ? $"Preventivo {quote.QuoteNumber} — cliente fornitore: conteggiata solo la manodopera"
            : $"Preventivo {quote.QuoteNumber} — {quote.CustomerName}";
        double revenue = customerIsSupplier
            ? quote.Labors.Sum(labor => labor.TotalPrice) *
              (1 - Math.Clamp(quote.LaborDiscount, 0, 100) / 100)
            : quote.Imponibile;
        TxtRevenue.Text = revenue.ToString("0.00", CultureInfo.CurrentCulture);
        TxtSupplierDiscount.Text = supplierDiscount.ToString("0.##", CultureInfo.CurrentCulture);
        TabMaterials.IsEnabled = !customerIsSupplier;
        ShowResult(RealProfitCalculator.Calculate(BuildInput()));
    }

    private RealProfitInput BuildInput() => new()
    {
        QuoteRevenue = ParseNonNegative(TxtRevenue.Text, "ricavo imponibile"),
        ExcludeMaterials = _excludeMaterials,
        SupplierDiscount = ParsePercentage(TxtSupplierDiscount.Text, "sconto fornitore"),
        Workers = (int)ParseNonNegative(TxtWorkers.Text, "numero operai"),
        Days = ParseNonNegative(TxtDays.Text, "giorni"),
        HoursPerDay = ParseNonNegative(TxtHoursPerDay.Text, "ore al giorno"),
        HourlyCost = ParseNonNegative(TxtHourlyCost.Text, "costo orario"),
        Materials = _materials.ToList(),
        CompanyMaterials = _companyMaterials
            .Where(item => item.Total != 0 || !string.IsNullOrWhiteSpace(item.Name))
            .ToList()
    };

    private void OnCalculateClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ShowResult(RealProfitCalculator.Calculate(BuildInput()));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Dati non validi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowResult(RealProfitResult result)
    {
        TxtCustomerMaterials.Text = $"{result.CustomerMaterialRevenue:N2} €";
        TxtSupplierMaterials.Text = $"{result.SupplierMaterialCost:N2} €";
        TxtOtherCosts.Text = $"{result.LaborCost + result.CompanyMaterialCost:N2} €";
        TxtProfit.Text = $"{result.Profit:N2} € ({result.ProfitPercentage:N1}%)";
        TxtProfit.Foreground = result.Profit >= 0
            ? System.Windows.Media.Brushes.ForestGreen
            : System.Windows.Media.Brushes.Firebrick;
    }

    private void OnAddCompanyCostClick(object sender, RoutedEventArgs e)
    {
        var item = new CompanyMaterialCost { Name = "Nuovo costo", Quantity = 1 };
        _companyMaterials.Add(item);
        GridCompanyMaterials.SelectedItem = item;
        GridCompanyMaterials.ScrollIntoView(item);
    }

    private void OnCompanyMaterialSearchKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        string query = CboCompanyMaterialSearch.Text?.Trim() ?? string.Empty;
        CboCompanyMaterialSearch.ItemsSource = string.IsNullOrWhiteSpace(query)
            ? _availableCompanyMaterials
            : _availableCompanyMaterials
                .Where(material => material.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        CboCompanyMaterialSearch.IsDropDownOpen = true;
    }

    private void OnCompanyMaterialSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CboCompanyMaterialSearch.SelectedItem is Item)
            AddSelectedCompanyMaterial();
    }

    private void OnAddSelectedCompanyMaterialClick(object sender, RoutedEventArgs e) =>
        AddSelectedCompanyMaterial();

    private void AddSelectedCompanyMaterial()
    {
        if (CboCompanyMaterialSearch.SelectedItem is not Item material)
            return;

        var existing = _companyMaterials.FirstOrDefault(item =>
            string.Equals(item.Name, material.Name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Quantity += 1;
            GridCompanyMaterials.SelectedItem = existing;
            GridCompanyMaterials.ScrollIntoView(existing);
        }
        else
        {
            var item = new CompanyMaterialCost
            {
                Name = material.Name,
                Quantity = 1,
                UnitCost = material.UnitPrice
            };
            _companyMaterials.Add(item);
            GridCompanyMaterials.SelectedItem = item;
            GridCompanyMaterials.ScrollIntoView(item);
        }

        CboCompanyMaterialSearch.SelectedItem = null;
        CboCompanyMaterialSearch.Text = string.Empty;
        CboCompanyMaterialSearch.ItemsSource = _availableCompanyMaterials;
    }

    private void OnRemoveCompanyCostClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is CompanyMaterialCost item)
            _companyMaterials.Remove(item);
    }

    private static double ParseNonNegative(string? text, string field)
    {
        if (!TryParse(text, out double value) || value < 0)
            throw new InvalidOperationException($"Inserisci un valore valido per {field}.");
        return value;
    }

    private static double ParsePercentage(string? text, string field)
    {
        double value = ParseNonNegative(text, field);
        if (value > 100)
            throw new InvalidOperationException($"{field} deve essere compreso tra 0 e 100.");
        return value;
    }

    private static bool TryParse(string? text, out double value)
    {
        string normalized = (text ?? string.Empty).Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
