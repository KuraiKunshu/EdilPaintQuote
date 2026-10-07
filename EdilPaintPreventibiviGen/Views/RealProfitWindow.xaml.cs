using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Microsoft.Win32;

namespace EdilPaintPreventibiviGen.Views;

public partial class RealProfitWindow : Window
{
    public bool CloseAfterSaved { get; set; }
    private bool _isSaving;
    private bool _isExporting;
    private readonly QuoteHistoryEntry _quote;
    private readonly ObservableCollection<ProfitMaterialCost> _materials;
    private readonly ObservableCollection<CompanyMaterialCost> _companyMaterials = [];
    private readonly List<Item> _availableCompanyMaterials;
    private readonly Func<RealProfitSnapshot, Task> _saveCalculation;

    private readonly bool _excludeMaterials;
    private CalendarLaborSnapshot? _calendarLabor;
    private bool _scheduleCurrent = true;
    private bool _automaticSettingsCurrent = true;
    private bool _automaticCostsValid = true;
    private bool _closed;
    private readonly SemaphoreSlim _sourceRefreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _calendarRefreshTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    public RealProfitWindow(
        QuoteHistoryEntry quote,
        double supplierDiscount,
        bool customerIsSupplier,
        IEnumerable<Item> companyMaterials,
        Func<RealProfitSnapshot, Task> saveCalculation,
        RealProfitSettingsModel? defaults = null)
    {
        InitializeComponent();
        Closing += (_, args) => { if (CloseAfterSaved && _isSaving) args.Cancel = true; };
        defaults ??= new RealProfitSettingsModel();
        defaults.Normalize();
        _quote = quote;
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _closed = true;
            _calendarRefreshTimer.Stop();
            _lifetime.Cancel();
        };
        _calendarRefreshTimer.Tick += async (_, _) =>
        {
            if (_isSaving || _isExporting || _sourceRefreshGate.CurrentCount == 0) return;
            await RefreshCostSourcesAsync(refreshMaterials: false);
        };
        _saveCalculation = saveCalculation ?? throw new ArgumentNullException(nameof(saveCalculation));
        RealProfitSnapshot? savedCalculation = quote.RealProfit;
        RealProfitInput? savedInput = savedCalculation?.Input;
        _calendarLabor = savedInput?.CalendarLabor?.CreateCopy();
        _scheduleCurrent = App.WorkSchedule == null;
        _excludeMaterials = savedInput?.ExcludeMaterials ?? customerIsSupplier;
        _automaticSettingsCurrent = _excludeMaterials || App.AutomaticMaterials == null;
        _availableCompanyMaterials = companyMaterials
            .Where(material => material.IsCompanyMaterial)
            .OrderBy(material => material.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        IEnumerable<ProfitMaterialCost> materialCosts = savedInput == null
            ? quote.Materials.Select(material => new ProfitMaterialCost
            {
                Name = material.Name,
                Quantity = material.Quantity,
                UnitOfMeasure = material.UnitOfMeasure,
                CustomerUnitPrice = material.UnitPrice,
                CustomerDiscount = 100 -
                    (1 - Math.Clamp(material.Discount, 0, 100) / 100) *
                    (1 - Math.Clamp(quote.MaterialDiscount, 0, 100) / 100) *
                    100
            })
            : (savedInput.Materials ?? []).Select(material => new ProfitMaterialCost
            {
                Name = material.Name,
                Quantity = material.Quantity,
                UnitOfMeasure = material.UnitOfMeasure,
                CustomerUnitPrice = material.CustomerUnitPrice,
                CustomerDiscount = material.CustomerDiscount
            });
        _materials = new ObservableCollection<ProfitMaterialCost>(materialCosts);

        if (savedInput != null)
        {
            foreach (CompanyMaterialCost material in savedInput.CompanyMaterials ?? [])
            {
                _companyMaterials.Add(new CompanyMaterialCost
                {
                    Name = material.Name,
                    Quantity = material.Quantity,
                    UnitOfMeasure = material.UnitOfMeasure,
                    UnitCost = material.UnitCost,
                    Source = material.Source
                });
            }
        }

        if (!_excludeMaterials && savedInput == null && App.AutomaticMaterials == null && App.AppSettings?.Business.EnableWindowAutomations != false)
            AddAutomaticWindowMaterials(defaults);

        GridMaterialCosts.ItemsSource = _materials;
        CboCompanyMaterialSearch.ItemsSource = _availableCompanyMaterials;
        GridCompanyMaterials.ItemsSource = _companyMaterials;
        TxtQuoteInfo.Text = _excludeMaterials
            ? $"Preventivo {quote.QuoteNumber} — cliente fornitore: materiali acquistati esclusi dal calcolo"
            : $"Preventivo {quote.QuoteNumber} — {quote.CustomerName}";
        double defaultRevenue = customerIsSupplier
            ? quote.Labors.Sum(labor => labor.TotalPrice) *
              (1 - Math.Clamp(quote.LaborDiscount, 0, 100) / 100)
            : quote.Imponibile;
        TxtRevenue.Text = (savedInput?.QuoteRevenue ?? defaultRevenue)
            .ToString("0.00", CultureInfo.CurrentCulture);
        TxtSupplierDiscount.Text = (savedInput?.SupplierDiscount ?? supplierDiscount)
            .ToString("0.##", CultureInfo.CurrentCulture);
        TxtProfitReduction.Text = (savedInput?.ProfitReductionPercentage ?? defaults.ProfitReductionPercentage)
            .ToString("0.##", CultureInfo.CurrentCulture);
        TxtWorkers.Text = (savedInput?.Workers ?? defaults.Workers)
            .ToString(CultureInfo.CurrentCulture);
        TxtDays.Text = (savedInput?.Days ?? defaults.Days)
            .ToString("0.##", CultureInfo.CurrentCulture);
        TxtHoursPerDay.Text = (savedInput?.HoursPerDay ?? defaults.HoursPerDay)
            .ToString("0.##", CultureInfo.CurrentCulture);
        TxtHourlyCost.Text = (savedInput?.HourlyCost ?? defaults.HourlyCost)
            .ToString("0.##", CultureInfo.CurrentCulture);
        TabMaterials.IsEnabled = !_excludeMaterials;
        ShowCalendarCrew();

        if (savedCalculation != null)
        {
            ShowResult(savedCalculation.Result);
            ShowSavedStatus(savedCalculation);
        }
        else
        {
            ShowResult(RealProfitCalculator.Calculate(BuildInput()));
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshCostSourcesAsync();
        if (!_closed) _calendarRefreshTimer.Start();
    }

    private async Task RefreshCostSourcesAsync(bool refreshMaterials = true)
    {
        try
        {
            await _sourceRefreshGate.WaitAsync(_lifetime.Token);
            try
            {
                if (App.WorkSchedule != null)
                {
                    var snapshot = await App.WorkSchedule.GetQuoteLaborAsync(_quote.QuoteNumber, _lifetime.Token);
                    _scheduleCurrent = snapshot.IsCurrent;
                    if (snapshot.IsCurrent || snapshot.HasCachedData) _calendarLabor = snapshot.Labor;
                    ShowCalendarCrew();
                }
                if (refreshMaterials && !_excludeMaterials && App.AutomaticMaterials != null)
                {
                    var snapshot = await App.AutomaticMaterials.GetLatestAsync(_lifetime.Token);
                    _automaticSettingsCurrent = snapshot.IsCurrent;
                    if (snapshot.IsCurrent)
                    {
                        var labors = await App.DataService.GetLaborCatalogAsync().WaitAsync(_lifetime.Token);
                        var materials = await App.DataService.GetPersonalMaterialsAsync().WaitAsync(_lifetime.Token);
                        _automaticSettingsCurrent = App.DataService.CanSynchronize;
                        if (_automaticSettingsCurrent)
                        {
                            var result = RealProfitAutomaticMaterialBuilder.Build(_quote, snapshot.Settings, labors, materials,
                                enableWindowAutomations: App.AppSettings?.Business.EnableWindowAutomations != false);
                            _automaticCostsValid = !result.HasWarnings;
                            if (_automaticCostsValid)
                            {
                                // Shared automatic rows are rebuilt; manually entered costs keep their values.
                                foreach (var row in _companyMaterials.Where(row =>
                                    string.Equals(row.Source?.Trim(), "Automatico", StringComparison.OrdinalIgnoreCase)).ToArray())
                                    _companyMaterials.Remove(row);
                                foreach (var row in result.Materials) _companyMaterials.Add(row);
                            }
                            _availableCompanyMaterials.Clear();
                            _availableCompanyMaterials.AddRange(materials.Where(material => material.IsCompanyMaterial)
                                .OrderBy(material => material.Name, StringComparer.OrdinalIgnoreCase));
                            if (CboCompanyMaterialSearch.SelectedItem == null)
                                CboCompanyMaterialSearch.ItemsSource = _availableCompanyMaterials;
                            if (result.Notices.Count > 0)
                                ShowAutomaticMaterialNotice((result.HasWarnings
                                    ? "Calcolo automatico incompleto: i costi precedenti sono conservati. Correggi le regole prima di salvare o esportare.\n" : "")
                                    + string.Join(Environment.NewLine, result.Notices), result.HasWarnings);
                            else
                            {
                                ScrollAutomaticMaterialsInfo.Visibility = Visibility.Collapsed;
                                TxtCompanyCostsHint.Visibility = Visibility.Visible;
                            }
                        }
                    }
                }
                if (!_closed)
                {
                    if (!_automaticCostsValid)
                    {
                        TxtSaveStatus.Text = "Calcolo automatico incompleto: verifica i materiali segnalati prima di salvare o esportare.";
                        TxtSaveStatus.Foreground = (Brush)FindResource("DangerRedBrush");
                    }
                    else if (!_scheduleCurrent || !_automaticSettingsCurrent)
                    {
                        TxtSaveStatus.Text = "Database non aggiornato: ripristina la connessione prima di salvare o esportare i costi.";
                        TxtSaveStatus.Foreground = (Brush)FindResource("DangerRedBrush");
                    }
                    try
                    {
                        var input = BuildInput();
                        ShowResult(RealProfitCalculator.Calculate(input));
                        if (_scheduleCurrent && _automaticSettingsCurrent && _automaticCostsValid &&
                            JsonSerializer.Serialize(input) != JsonSerializer.Serialize(_quote.RealProfit?.Input))
                        {
                            TxtSaveStatus.Text = "Costi aggiornati. Salva il calcolo per conservarne il riepilogo.";
                            TxtSaveStatus.Foreground = (Brush)FindResource("PrimaryBlueBrush");
                        }
                    }
                    catch (InvalidOperationException) { /* Keep editable draft values while refreshing the calendar. */ }
                }
            }
            finally { _sourceRefreshGate.Release(); }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _scheduleCurrent = false;
            if (refreshMaterials) _automaticSettingsCurrent = false;
            if (!_closed)
            {
                ShowCalendarCrew();
                TxtSaveStatus.Text = $"Aggiornamento dei costi non riuscito: {ex.Message}";
                TxtSaveStatus.Foreground = (Brush)FindResource("DangerRedBrush");
            }
        }
    }

    private void ShowCalendarCrew()
    {
        bool fromCalendar = _calendarLabor != null;
        ManualWorkersPanel.Visibility = ManualDaysPanel.Visibility = ManualHoursPanel.Visibility =
            fromCalendar ? Visibility.Collapsed : Visibility.Visible;
        CalendarCrewPanel.Visibility = fromCalendar ? Visibility.Visible : Visibility.Collapsed;
        TxtCrewTitle.Text = fromCalendar ? "Squadra assegnata nel calendario" : "Persone, tempo e costo";
        TxtCrewHint.Text = fromCalendar
            ? "Persone e ore seguono gli interventi salvati dell’ordine. Imposta il costo orario per persona."
            : "Nessun intervento nel calendario: compila i valori operativi per la stima.";
        TxtCrewSource.Text = fromCalendar
            ? "Il costo somma le ore di ciascun dipendente negli interventi dell’ordine, anche quando la squadra cambia."
            : "Indica quante persone lavorano, per quanti giorni e a quale costo orario per persona.";
        TxtCrewFormula.Text = fromCalendar
            ? "Costo della squadra = ore totali delle persone assegnate × costo orario per persona."
            : "Costo della squadra = persone × giorni × ore al giorno × costo orario per persona.";
        if (!fromCalendar) return;
        var labor = _calendarLabor!;
        TxtCalendarCrewTotal.Text = $"{labor.TotalPersonHours:0.##} ore/persona · {labor.DistinctWorkers} persone · {labor.Days} giorni";
        TxtCalendarCrewStatus.Text = !_scheduleCurrent ? "Copia precedente: calendario non aggiornato dal database."
            : labor.HasUnassignedInterventions ? "Attenzione: gli interventi senza dipendenti non aggiungono costo alla squadra."
            : "Aggiornato dal calendario condiviso. La pausa della giornata intera è esclusa.";
        CalendarCrewRows.ItemsSource = labor.Rows.Select(row => new
        {
            Heading = $"{row.Date:dd/MM/yyyy} · {row.StartMinutes / 60:00}:{row.StartMinutes % 60:00}–{row.EndMinutes / 60:00}:{row.EndMinutes % 60:00} · {row.WorkHours:0.##} h × {row.WorkerCount} = {row.PersonHours:0.##} ore/persona",
            Crew = row.Employees.Count == 0 ? "Nessun dipendente assegnato" : string.Join(", ", row.Employees.Select(employee => employee.Name))
        }).ToList();
    }

    private void EnsureCurrentCostSources()
    {
        if (!_scheduleCurrent || !_automaticSettingsCurrent)
            throw new InvalidOperationException("Il calendario o le regole dei materiali non sono aggiornati dal database. Ripristina la connessione prima di salvare o esportare i costi.");
        if (!_automaticCostsValid)
            throw new InvalidOperationException("Il calcolo dei materiali automatici è incompleto. Correggi le regole e i materiali segnalati prima di salvare o esportare i costi.");
    }

    private RealProfitInput BuildInput()
    {
        if (!GridCompanyMaterials.CommitEdit(DataGridEditingUnit.Cell, true) || !GridCompanyMaterials.CommitEdit(DataGridEditingUnit.Row, true))
            throw new InvalidOperationException("Correggi le quantità evidenziate: usa valori positivi con al massimo 9 decimali.");
        if (_companyMaterials.Any(item => !QuantityValue.IsValid(item.Quantity)))
            throw new InvalidOperationException("I costi aziendali richiedono quantità positive con al massimo 9 decimali.");
        return new RealProfitInput
    {
        QuoteRevenue = ParseNonNegative(TxtRevenue.Text, "ricavo imponibile"),
        ProfitReductionPercentage = ParsePercentage(TxtProfitReduction.Text, "riduzione prudenziale"),
        ExcludeMaterials = _excludeMaterials,
        SupplierDiscount = ParsePercentage(TxtSupplierDiscount.Text, "sconto fornitore"),
        Workers = _calendarLabor?.DistinctWorkers ?? (int)ParseNonNegative(TxtWorkers.Text, "numero operai"),
        Days = _calendarLabor?.Days ?? ParseNonNegative(TxtDays.Text, "giorni"),
        HoursPerDay = _calendarLabor == null ? ParseNonNegative(TxtHoursPerDay.Text, "ore al giorno") : 0,
        HourlyCost = ParseNonNegative(TxtHourlyCost.Text, "costo orario"),
        CalendarLabor = _calendarLabor?.CreateCopy(),
        Materials = _materials.ToList(),
        CompanyMaterials = _companyMaterials
            .Where(item => item.Total != 0 || !string.IsNullOrWhiteSpace(item.Name))
            .ToList()
    };
    }

    private async void OnCalculateClick(object sender, RoutedEventArgs e)
    {
        if (_isSaving || _isExporting) return;
        _isSaving = true;
        BtnCalculate.IsEnabled = false;
        if (CloseAfterSaved) IsEnabled = false;
        RealProfitInput currentInput;
        RealProfitResult currentResult;
        try
        {
            await RefreshCostSourcesAsync();
            if (_closed) { _isSaving = false; return; }
            EnsureCurrentCostSources();
            currentInput = BuildInput();
            currentResult = RealProfitCalculator.Calculate(currentInput);
            ShowResult(currentResult);
        }
        catch (Exception ex)
        {
            _isSaving = false;
            BtnCalculate.IsEnabled = true;
            if (CloseAfterSaved) IsEnabled = true;
            MessageBox.Show(ex.Message, "Dati non validi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnCalculate.IsEnabled = false;
        _isSaving = true;
        if (CloseAfterSaved) IsEnabled = false;
        TxtSaveStatus.Text = "Salvataggio del calcolo in corso...";
        TxtSaveStatus.Foreground = (Brush)FindResource("PrimaryBlueBrush");
        try
        {
            var snapshot = new RealProfitSnapshot
            {
                CalculatedAtUtc = DateTime.UtcNow,
                CalculatedByDevice = DeviceNameService.GetCurrentDeviceName(),
                Input = CloneRealProfitInput(currentInput),
                Result = currentResult
            };

            await _saveCalculation(snapshot);
            _quote.RealProfit = snapshot;
            ShowSavedStatus(snapshot);
            _isSaving = false;
            if (CloseAfterSaved) { IsEnabled = true; Close(); }
        }
        catch (Exception ex)
        {
            TxtSaveStatus.Text = "Calcolo aggiornato, ma non salvato.";
            TxtSaveStatus.Foreground = (Brush)FindResource("DangerRedBrush");
            MessageBox.Show(
                $"Il guadagno è stato ricalcolato, ma non è stato possibile salvarlo.\n\n{ex.Message}",
                "Salvataggio non riuscito",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isSaving = false;
            if (CloseAfterSaved) IsEnabled = true;
            BtnCalculate.IsEnabled = true;
        }
    }

    private async void OnExportPdfClick(object sender, RoutedEventArgs e)
    {
        if (_isExporting || _isSaving) return;
        _isExporting = true;
        System.Windows.Controls.Button? exportButton = sender as System.Windows.Controls.Button;
        if (exportButton != null) exportButton.IsEnabled = false;
        try
        {
            await RefreshCostSourcesAsync();
            if (_closed) return;
            EnsureCurrentCostSources();
            RealProfitInput currentInput = CloneRealProfitInput(BuildInput());
            RealProfitResult currentResult = RealProfitCalculator.Calculate(currentInput);
            ShowResult(currentResult);

            string safeQuoteNumber = StoragePathService.SanitizeFolderName(_quote.QuoteNumber);
            string safeCustomer = StoragePathService.SanitizeFolderName(_quote.CustomerName);
            if (safeCustomer.Length > 60)
                safeCustomer = safeCustomer[..60].Trim();
            string suggestedName = $"CostiGuadagno_Preventivo_{safeQuoteNumber}_{safeCustomer}.pdf";

            var dialog = new SaveFileDialog
            {
                Title = "Salva riepilogo costi e guadagno",
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

            var company = await App.DataService.GetCompanyAsync();
            var context = new RealProfitPdfContext
            {
                CompanyName = company?.Nome ?? string.Empty,
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
                $"PDF dei costi e del guadagno creato correttamente.\n\n{dialog.FileName}",
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
                $"Impossibile creare il PDF dei costi e del guadagno.\n\n{ex.Message}",
                "Errore PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isExporting = false;
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
        CalendarLabor = source.CalendarLabor?.CreateCopy(),
        Materials = source.Materials.Select(material => new ProfitMaterialCost
        {
            Name = material.Name,
            Quantity = material.Quantity,
            UnitOfMeasure = material.UnitOfMeasure,
            CustomerUnitPrice = material.CustomerUnitPrice,
            CustomerDiscount = material.CustomerDiscount
        }).ToList(),
        CompanyMaterials = source.CompanyMaterials.Select(material => new CompanyMaterialCost
        {
            Name = material.Name,
            Quantity = material.Quantity,
            UnitOfMeasure = material.UnitOfMeasure,
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
        TxtOtherCosts.ToolTip = $"Costo della squadra: {result.LaborCost:N2} €\nAltri costi del lavoro: {result.CompanyMaterialCost:N2} €";
        TxtProfit.Text = $"{result.Profit:N2} € ({result.ProfitPercentage:N1}%)";
        if (result.ProfitReductionAmount > 0)
        {
            TxtProfitBreakdown.Text =
                $"Guadagno iniziale: {result.ProfitBeforeReduction:N2} € · Riduzione: −{result.ProfitReductionAmount:N2} €";
            TxtProfitBreakdown.Visibility = Visibility.Visible;
        }
        else
        {
            TxtProfitBreakdown.Visibility = Visibility.Collapsed;
        }

        bool isProfit = result.Profit >= 0;
        Brush resultBrush = (Brush)FindResource(isProfit ? "SuccessGreenBrush" : "DangerRedBrush");
        TxtProfitState.Text = isProfit ? "GUADAGNO STIMATO" : "PERDITA STIMATA";
        TxtProfitState.Foreground = resultBrush;
        TxtProfit.Foreground = resultBrush;
        ProfitCard.BorderBrush = resultBrush;
    }

    private void ShowSavedStatus(RealProfitSnapshot snapshot)
    {
        DateTime savedAt = snapshot.CalculatedAtUtc.ToLocalTime();
        string device = string.IsNullOrWhiteSpace(snapshot.CalculatedByDevice)
            ? string.Empty
            : $" da {snapshot.CalculatedByDevice}";
        TxtSaveStatus.Text = $"Salvato il {savedAt:dd/MM/yyyy 'alle' HH:mm}{device}.";
        TxtSaveStatus.Foreground = (Brush)FindResource("SuccessGreenBrush");
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
                    .Select(material => new AutomaticWindowProductLine(material.Name, material.Quantity, material.UnitOfMeasure))
                    .ToArray(),
                Labors = _quote.Labors
                    .Select(labor => new AutomaticWindowLaborLine(
                        labor.PersistentId,
                        labor.Name,
                        labor.Quantity, labor.UnitOfMeasure))
                    .ToArray(),
                ExistingQuoteMaterials = _quote.Materials
                    .Select(material => new AutomaticQuoteMaterialLine(
                        material.PersistentId,
                        material.Name,
                        material.Quantity, material.UnitOfMeasure))
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
                        material.Name, material.UnitOfMeasure))
                    .ToArray()
            });

        // Nessuna lavorazione associata alle regole è presente nel preventivo.
        if (calculation.RuleCalculations.Count == 0 && calculation.Issues.Count == 0)
            return;

        var notices = new List<string>();
        var localWarnings = new List<string>();
        AutomaticWindowMaterialPlanLine[] quantitiesTooLarge = calculation.Materials
            .Where(material => material.QuantityToAdd > 0 && !QuantityValue.IsValid(material.QuantityToAdd))
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
                    Quantity = materialPlan.QuantityToAdd,
                    UnitOfMeasure = catalogMaterial?.UnitOfMeasure ?? "pz",
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
                UnitOfMeasure = material.UnitOfMeasure,
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
