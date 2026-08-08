using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Microsoft.Win32;

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
        IEnumerable<Item> companyMaterials,
        RealProfitSettingsModel? defaults = null)
    {
        InitializeComponent();
        defaults ??= new RealProfitSettingsModel();
        defaults.Normalize();
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

        if (!_excludeMaterials)
            AddAutomaticWindowMaterials(defaults);

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
        TxtProfitReduction.Text = defaults.ProfitReductionPercentage.ToString("0.##", CultureInfo.CurrentCulture);
        TxtWorkers.Text = defaults.Workers.ToString(CultureInfo.CurrentCulture);
        TxtDays.Text = defaults.Days.ToString("0.##", CultureInfo.CurrentCulture);
        TxtHoursPerDay.Text = defaults.HoursPerDay.ToString("0.##", CultureInfo.CurrentCulture);
        TxtHourlyCost.Text = defaults.HourlyCost.ToString("0.##", CultureInfo.CurrentCulture);
        TabMaterials.IsEnabled = !customerIsSupplier;
        ShowResult(RealProfitCalculator.Calculate(BuildInput()));
    }

    private RealProfitInput BuildInput() => new()
    {
        QuoteRevenue = ParseNonNegative(TxtRevenue.Text, "ricavo imponibile"),
        ProfitReductionPercentage = ParsePercentage(TxtProfitReduction.Text, "riduzione prudenziale"),
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

    private async void OnExportPdfClick(object sender, RoutedEventArgs e)
    {
        System.Windows.Controls.Button? exportButton = sender as System.Windows.Controls.Button;
        try
        {
            RealProfitInput currentInput = BuildInput();
            RealProfitResult currentResult = RealProfitCalculator.Calculate(currentInput);
            ShowResult(currentResult);

            string safeQuoteNumber = StoragePathService.SanitizeFolderName(_quote.QuoteNumber);
            string safeCustomer = StoragePathService.SanitizeFolderName(_quote.CustomerName);
            if (safeCustomer.Length > 60)
                safeCustomer = safeCustomer[..60].Trim();
            string suggestedName = $"GuadagnoReale_Preventivo_{safeQuoteNumber}_{safeCustomer}.pdf";

            var dialog = new SaveFileDialog
            {
                Title = "Salva PDF del guadagno reale",
                Filter = "Documento PDF (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = suggestedName
            };
            string? quoteFolder = Path.GetDirectoryName(_quote.PdfPath);
            if (!string.IsNullOrWhiteSpace(quoteFolder) && Directory.Exists(quoteFolder))
                dialog.InitialDirectory = quoteFolder;

            if (dialog.ShowDialog(this) != true)
                return;

            var context = new RealProfitPdfContext
            {
                QuoteNumber = _quote.QuoteNumber,
                QuoteDate = _quote.Date == default ? DateTime.Today : _quote.Date,
                CustomerName = _quote.CustomerName,
                CustomerIsSupplier = _excludeMaterials,
                GeneratedAt = DateTime.Now,
                Input = CloneRealProfitInput(currentInput),
                Result = currentResult
            };

            if (exportButton != null)
                exportButton.IsEnabled = false;
            await Task.Run(() => new PdfService().GenerateRealProfitPdf(context, dialog.FileName));

            MessageBox.Show(
                $"PDF del guadagno reale creato correttamente.\n\n{dialog.FileName}",
                "PDF creato",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            try
            {
                Process.Start(new ProcessStartInfo(
                    "explorer.exe",
                    $"/select,\"{dialog.FileName}\"")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RealProfitPdf] Impossibile mostrare il file: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Impossibile creare il PDF del guadagno reale.\n\n{ex.Message}",
                "Errore PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (exportButton != null)
                exportButton.IsEnabled = true;
        }
    }

    private static RealProfitInput CloneRealProfitInput(RealProfitInput source) => new()
    {
        QuoteRevenue = source.QuoteRevenue,
        ProfitReductionPercentage = source.ProfitReductionPercentage,
        ExcludeMaterials = source.ExcludeMaterials,
        SupplierDiscount = source.SupplierDiscount,
        Workers = source.Workers,
        Days = source.Days,
        HoursPerDay = source.HoursPerDay,
        HourlyCost = source.HourlyCost,
        Materials = source.Materials.Select(material => new ProfitMaterialCost
        {
            Name = material.Name,
            Quantity = material.Quantity,
            CustomerUnitPrice = material.CustomerUnitPrice,
            CustomerDiscount = material.CustomerDiscount
        }).ToList(),
        CompanyMaterials = source.CompanyMaterials.Select(material => new CompanyMaterialCost
        {
            Name = material.Name,
            Quantity = material.Quantity,
            UnitCost = material.UnitCost,
            Source = material.Source
        }).ToList()
    };

    private void ShowResult(RealProfitResult result)
    {
        TxtCustomerMaterials.Text = $"{result.MaterialMargin:N2} €";
        TxtMaterialMarginBreakdown.Text =
            $"Ricavo {result.CustomerMaterialRevenue:N2} € - Costo {result.SupplierMaterialCost:N2} €";
        Brush materialMarginBrush = (Brush)FindResource(
            result.MaterialMargin < 0 ? "DangerRedBrush" : "SuccessGreenBrush");
        TxtCustomerMaterials.Foreground = materialMarginBrush;
        MaterialMarginCard.BorderBrush = materialMarginBrush;
        TxtSupplierMaterials.Text = $"{result.SupplierMaterialCost:N2} €";
        TxtOtherCosts.Text = $"{result.LaborCost + result.CompanyMaterialCost:N2} €";
        TxtProfit.Text = $"{result.Profit:N2} € ({result.ProfitPercentage:N1}%)";
        if (result.ProfitReductionAmount > 0)
        {
            TxtProfitBreakdown.Text =
                $"Prima: {result.ProfitBeforeReduction:N2} € · Riduzione: −{result.ProfitReductionAmount:N2} €";
            TxtProfitBreakdown.Visibility = Visibility.Visible;
        }
        else
        {
            TxtProfitBreakdown.Visibility = Visibility.Collapsed;
        }

        bool isProfit = result.Profit >= 0;
        Brush resultBrush = (Brush)FindResource(isProfit ? "SuccessGreenBrush" : "DangerRedBrush");
        TxtProfitState.Text = isProfit ? "UTILE STIMATO" : "PERDITA STIMATA";
        TxtProfitState.Foreground = resultBrush;
        TxtProfit.Foreground = resultBrush;
        ProfitCard.BorderBrush = resultBrush;
    }

    private void AddAutomaticWindowMaterials(RealProfitSettingsModel settings)
    {
        if (!settings.WindowMaterialRules.Any(rule => rule.Enabled))
            return;

        string configuredCatalogIdentity = settings.WindowMaterialCatalogIdentity?.Trim() ?? string.Empty;
        string currentCatalogIdentity = App.AppSettings?.Database.GetCatalogIdentity() ?? string.Empty;
        if (configuredCatalogIdentity.Length > 0 &&
            !string.Equals(
                configuredCatalogIdentity,
                currentCatalogIdentity,
                StringComparison.OrdinalIgnoreCase))
        {
            ShowAutomaticMaterialNotice(
                "Regole materiali automatici non applicate: sono associate a un altro database. " +
                "Apri le Impostazioni, riseleziona lavorazioni e materiali dal catalogo corrente e salva.",
                isWarning: true);
            return;
        }

        AutomaticWindowMaterialCalculationResult calculation =
            AutomaticWindowMaterialCalculator.Calculate(new AutomaticWindowMaterialCalculationInput
            {
                WindowProducts = _quote.Materials
                    .Select(material => new AutomaticWindowProductLine(material.Name, material.Quantity))
                    .ToArray(),
                Labors = _quote.Labors
                    .Select(labor => new AutomaticWindowLaborLine(
                        labor.PersistentId,
                        labor.Name,
                        labor.Quantity))
                    .ToArray(),
                ExistingQuoteMaterials = _quote.Materials
                    .Select(material => new AutomaticQuoteMaterialLine(
                        material.PersistentId,
                        material.Name,
                        material.Quantity))
                    .ToArray(),
                Rules = settings.WindowMaterialRules
                    .Select((rule, index) => new AutomaticWindowMaterialRule
                    {
                        RuleId = $"regola-{index + 1}",
                        Enabled = rule.Enabled,
                        IsWindowAutomation = rule.IsWindowAutomation,
                        LaborCatalogItemId = rule.LaborCatalogId.GetValueOrDefault(),
                        LaborNameSnapshot = rule.LaborName,
                        MaterialCatalogItemId = rule.MaterialCatalogId.GetValueOrDefault(),
                        MaterialNameSnapshot = rule.MaterialName,
                        Mode = rule.CalculationMode,
                        Parameter = rule.QuantityParameter
                    })
                    .ToArray(),
                WindowPrefixes = settings.WindowProductPrefixes,
                MaterialCatalog = _availableCompanyMaterials
                    .Select(material => new AutomaticMaterialCatalogItem(
                        material.PersistentId,
                        material.Name))
                    .ToArray()
            });

        // Nessuna lavorazione associata alle regole è presente nel preventivo.
        if (calculation.RuleCalculations.Count == 0 && calculation.Issues.Count == 0)
            return;

        var notices = new List<string>();
        var localWarnings = new List<string>();
        AutomaticWindowMaterialPlanLine[] quantitiesTooLarge = calculation.Materials
            .Where(material => material.QuantityToAdd > int.MaxValue)
            .ToArray();
        bool canApplyAllAutomaticMaterials = quantitiesTooLarge.Length == 0;
        if (!canApplyAllAutomaticMaterials)
        {
            string names = string.Join(", ", quantitiesTooLarge.Select(material => material.MaterialName));
            localWarnings.Add(
                $"La quantità calcolata supera il limite supportato per: {names}. " +
                "Per evitare un calcolo parziale, nessun materiale automatico è stato aggiunto.");
        }

        foreach (AutomaticWindowMaterialPlanLine materialPlan in calculation.Materials)
        {
            Item? catalogMaterial = ResolveCompanyMaterial(materialPlan);
            if (materialPlan.QuantityToAdd > 0 && canApplyAllAutomaticMaterials)
            {
                _companyMaterials.Add(new CompanyMaterialCost
                {
                    Name = catalogMaterial?.Name ?? materialPlan.MaterialName,
                    Quantity = (int)materialPlan.QuantityToAdd,
                    UnitCost = Math.Max(0, catalogMaterial?.UnitPrice ?? 0),
                    Source = "Automatico"
                });
            }
            string calculationDetails = string.Join(
                "; ",
                calculation.RuleCalculations
                    .Where(rule => materialPlan.ContributingRuleIds.Contains(
                        rule.RuleId,
                        StringComparer.OrdinalIgnoreCase))
                    .Select(FormatAutomaticRuleCalculation));
            string quantityNote = materialPlan.QuantityToAdd > 0 && canApplyAllAutomaticMaterials
                ? $"aggiunta automaticamente: {materialPlan.QuantityToAdd}"
                : "nessuna quantità automatica aggiunta";
            if (materialPlan.AlreadyQuotedQuantity > 0)
            {
                quantityNote =
                    $"già nel preventivo: {materialPlan.AlreadyQuotedQuantity}; {quantityNote}";
            }

            string costNote = materialPlan.QuantityToAdd > 0 && !canApplyAllAutomaticMaterials
                ? "costo automatico non applicato"
                : materialPlan.QuantityToAdd == 0
                ? "nessun costo aggiuntivo necessario"
                : catalogMaterial == null
                    ? "costo non trovato: inseriscilo nella tabella"
                    : $"costo catalogo {catalogMaterial.UnitPrice:N2} €/unità";
            notices.Add(
                $"• {catalogMaterial?.Name ?? materialPlan.MaterialName}: {calculationDetails}. " +
                $"Fabbisogno {materialPlan.GrossRequiredQuantity} unità; {quantityNote}; {costNote}.");
        }

        string[] issueMessages = calculation.Issues
            .Select(issue => issue.Message)
            .Concat(localWarnings)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (issueMessages.Length > 0)
            notices.Add($"Attenzione: {string.Join(" ", issueMessages)}");

        if (notices.Count == 0)
            return;

        ShowAutomaticMaterialNotice(
            $"Calcolo automatico iniziale:{Environment.NewLine}{string.Join(Environment.NewLine, notices)}",
            isWarning: issueMessages.Length > 0 || calculation.Materials.Any(material =>
                ResolveCompanyMaterial(material) == null));
    }

    private Item? ResolveCompanyMaterial(AutomaticWindowMaterialPlanLine materialPlan)
    {
        if (materialPlan.MaterialResolution == AutomaticMaterialResolutionStatus.AmbiguousName)
            return null;

        if (materialPlan.MaterialCatalogItemId > 0)
        {
            Item[] idMatches = _availableCompanyMaterials
                .Where(material => material.PersistentId == materialPlan.MaterialCatalogItemId)
                .Take(2)
                .ToArray();
            return idMatches.Length == 1 ? idMatches[0] : null;
        }

        Item[] exactMatches = _availableCompanyMaterials
            .Where(material => string.Equals(
                material.Name.Trim(),
                materialPlan.MaterialName.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return exactMatches.Length == 1 ? exactMatches[0] : null;
    }

    private static string FormatAutomaticRuleCalculation(
        AutomaticWindowMaterialRuleCalculation calculation)
    {
        string labor = string.IsNullOrWhiteSpace(calculation.LaborName)
            ? $"lavorazione ID {calculation.LaborCatalogItemId}"
            : calculation.LaborName;
        if (!calculation.IsWindowAutomation)
        {
            return $"{labor} (automazione generica: {calculation.Parameter:0.###} × " +
                   $"quantità lavorazione {calculation.LaborQuantity}) = " +
                   $"{calculation.GrossRequiredQuantity} unità";
        }

        bool isFixed = calculation.Mode == AutomaticWindowMaterialModes.FixedPerWindow;
        string mode = isFixed
            ? $"quantità fissa {calculation.Parameter:0.###}/finestra"
            : $"perimetro ×{calculation.Parameter:0.###} unità/m";
        string sizes = string.Join(
            ", ",
            calculation.Details.Select(detail =>
            {
                string windowLabel = detail.WindowQuantity == 1 ? "finestra" : "finestre";
                string source = isFixed
                    ? string.Empty
                    : $"perimetro {2m * (detail.Size.WidthCentimeters + detail.Size.HeightCentimeters) / 100m:0.##} m → ";
                return $"{detail.WindowQuantity} {windowLabel} " +
                       $"{detail.Size.WidthCentimeters}×{detail.Size.HeightCentimeters}: " +
                       $"{source}{detail.RoundedQuantityPerWindow} unità ciascuna = " +
                       $"{detail.RequiredQuantity} unità";
            }));
        return $"{labor} ({mode}; quantità lavorazione {calculation.LaborQuantity}) — {sizes}";
    }

    private void ShowAutomaticMaterialNotice(string message, bool isWarning)
    {
        TxtAutomaticMaterialsInfo.Text = message;
        TxtAutomaticMaterialsInfo.Foreground = (Brush)FindResource(
            isWarning ? "DangerRedBrush" : "PrimaryBlueBrush");
        TxtCompanyCostsHint.Visibility = Visibility.Collapsed;
        ScrollAutomaticMaterialsInfo.Visibility = Visibility.Visible;
        TabCompanyCosts.IsSelected = true;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
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
            !string.Equals(item.Source, "Automatico", StringComparison.OrdinalIgnoreCase) &&
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
        if (!TryParse(text, out double value) || !double.IsFinite(value) || value < 0)
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
