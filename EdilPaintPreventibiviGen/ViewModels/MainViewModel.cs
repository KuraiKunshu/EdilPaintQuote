using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using EdilPaintPreventibiviGen.Views;

namespace EdilPaintPreventibiviGen.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    #region Services
    private readonly IDataService _dataService;
    private readonly VeluxService _veluxService = new();
    private readonly PdfService _pdfService = new();
    private readonly StoragePathService _storagePathService = StoragePathService.Instance;
    private readonly QuoteCalculator _quoteCalculator = new();
    private readonly QuoteHistoryService _quoteHistoryService;
    #endregion

    #region Data Collections
    private Company _companyData = new();
    private List<Customer> _allCustomers = new();
    private List<Item> _allCatalogLabors = new();
    private List<Item> _personalMaterials = new();
    private readonly HashSet<string> _significantMaterialPrefixes = new(StringComparer.OrdinalIgnoreCase);
    #endregion

    #region Selection State
    private Customer? _selectedCustomer;
    private Customer? _selectedSecondCustomer;
    private VeluxResult? _selectedCatalogMaterial;
    private Item? _selectedCatalogLabor;
    #endregion

    #region UI State - Search
    private string _customerSearchText = string.Empty;
    private string _secondCustomerSearchText = string.Empty;
    private string _laborSearchText = string.Empty;
    #endregion

    #region UI State - Flags
    private bool _isJointVenture;
    private bool _isSecondCustomerEnabled;
    private bool _isSavingQuoteHistory;
    private bool _isSavingHistory;
    private bool _isEditingExistingQuote;
    #endregion

    #region Quote Info
    private string _quoteNumber = "000000";
    private string _selectedLogo = string.Empty;
    private string _paymentTerms = string.Empty;
    private string _partnerCompanyName = string.Empty;
    #endregion

    #region Input Fields
    private string _inputName = string.Empty;
    private string _inputDescription = string.Empty;
    private double _inputValue;
    private int _inputQuantity = 1;
    private bool _isSignificant;
    #endregion

    #region Totals
    private double _materialDiscount;
    private double _laborDiscount;
    private string _ivaType = "RC 10%+22%";
    private double _imponibile;
    private double _ivaTotale;
    private double _totaleGenerale;
    #endregion

    #region UI Feedback
    private Brush _customerBorderBrush = Brushes.Red;
    private Brush _secondCustomerBorderBrush = Brushes.Red;
    #endregion

    #region Collections & Views
    public ObservableCollection<Customer> AllCustomers { get; } = new();
    public ObservableCollection<Item> AllCatalogLabors { get; } = new();
    public ObservableCollection<VeluxResult> AllCatalogMaterials { get; } = new();
    public ObservableCollection<Customer> FilteredCustomers { get; } = new();
    public ObservableCollection<Customer> FilteredSecondCustomers { get; } = new();
    public ObservableCollection<Item> FilteredLabors { get; } = new();
    public ObservableCollection<string> IvaOptions { get; } = new() { "RC 10%+22%", "10%", "22%", "esclusa" };
    public ObservableCollection<string> Logos { get; } = new();
    public ObservableCollection<Item> Materials { get; } = new();
    public ObservableCollection<Item> Labors { get; } = new();
    public ObservableCollection<SelectedAttachment> AttachedImages { get; } = new();
    public ObservableCollection<QuoteHistoryEntry> History { get; } = new();
    public ObservableCollection<QuoteHistorySummary> HistorySummaries { get; } = new();
    public ObservableCollection<CostAllocationItem> OurCosts { get; } = new();
    public ObservableCollection<CostAllocationItem> PartnerCosts { get; } = new();
    public ObservableCollection<CostAllocationItem> AdditionalCosts { get; } = new();
    public IReadOnlyList<Item> PersonalMaterialsView => _personalMaterials;

    public IEnumerable<QuoteStatus> StatusOptions => Enum.GetValues(typeof(QuoteStatus)).Cast<QuoteStatus>();
    #endregion

    #region Constructor
    public MainViewModel()
    {
        _dataService = App.DataService;
        _quoteHistoryService = new QuoteHistoryService(_dataService, _storagePathService);

        _veluxService.OnLoginRequired += HandleVeluxLogin;
        _ = LoadDataAsync();

        Materials.CollectionChanged += OnItemsCollectionChanged;
        Labors.CollectionChanged += OnItemsCollectionChanged;
    }
    #endregion

    #region Public Properties
    public bool IsSecondCustomerEnabled
    {
        get => _isSecondCustomerEnabled;
        set
        {
            _isSecondCustomerEnabled = value;
            if (!value)
                SelectedSecondCustomer = null;
            OnPropertyChanged();
        }
    }

    public Brush CustomerBorderBrush
    {
        get => _customerBorderBrush;
        set { _customerBorderBrush = value; OnPropertyChanged(); }
    }

    public Brush SecondCustomerBorderBrush
    {
        get => _secondCustomerBorderBrush;
        set { _secondCustomerBorderBrush = value; OnPropertyChanged(); }
    }

    public Customer? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (_selectedCustomer == value)
                return;

            _selectedCustomer = value;

            if (value != null)
            {
                ApplyCustomerDiscounts(value);
                CustomerBorderBrush = Brushes.Green;
            }
            else
            {
                MaterialDiscount = 0;
                LaborDiscount = 0;
                CustomerBorderBrush = Brushes.Red;
            }

            OnPropertyChanged();
        }
    }

    public Customer? SelectedSecondCustomer
    {
        get => _selectedSecondCustomer;
        set
        {
            _selectedSecondCustomer = value;
            SecondCustomerBorderBrush = value != null ? Brushes.Green : Brushes.Red;
            OnPropertyChanged();
        }
    }

    public VeluxResult? SelectedCatalogMaterial
    {
        get => _selectedCatalogMaterial;
        set
        {
            _selectedCatalogMaterial = value;
            if (value != null)
                _ = FetchVeluxDetails(value.Id);
            OnPropertyChanged();
        }
    }

    public Item? SelectedCatalogLabor
    {
        get => _selectedCatalogLabor;
        set
        {
            _selectedCatalogLabor = value;

            if (value != null)
            {
                InputName = value.Name;
                InputDescription = value.Description;
                InputValue = value.UnitPrice;

                OnPropertyChanged(nameof(InputName));
                OnPropertyChanged(nameof(InputDescription));
                OnPropertyChanged(nameof(InputValue));
            }

            OnPropertyChanged();
        }
    }

    public string QuoteNumber { get => _quoteNumber; set { _quoteNumber = value; OnPropertyChanged(); } }

    public string SelectedLogo
    {
        get => _selectedLogo;
        set
        {
            _selectedLogo = value;
            OnPropertyChanged();
            SaveCompanyData();
        }
    }

    public string PaymentTerms { get => _paymentTerms; set { _paymentTerms = value; OnPropertyChanged(); } }

    public string InputName { get => _inputName; set { _inputName = value; OnPropertyChanged(); } }
    public string InputDescription { get => _inputDescription; set { _inputDescription = value; OnPropertyChanged(); } }
    public double InputValue { get => _inputValue; set { _inputValue = value; OnPropertyChanged(); } }
    public int InputQuantity { get => _inputQuantity; set { _inputQuantity = value; OnPropertyChanged(); } }
    public bool IsSignificant { get => _isSignificant; set { _isSignificant = value; OnPropertyChanged(); } }

    public string PartnerCompanyName
    {
        get => _partnerCompanyName;
        set { _partnerCompanyName = value; OnPropertyChanged(); }
    }

    public double MaterialDiscount
    {
        get => _materialDiscount;
        set
        {
            if (_materialDiscount == value)
                return;
            _materialDiscount = value;
            OnPropertyChanged();
            CalculateTotals();
        }
    }

    public double LaborDiscount
    {
        get => _laborDiscount;
        set
        {
            if (_laborDiscount == value)
                return;
            _laborDiscount = value;
            OnPropertyChanged();
            CalculateTotals();
        }
    }

    public string IvaType
    {
        get => _ivaType;
        set
        {
            if (_ivaType == value)
                return;
            _ivaType = value;
            OnPropertyChanged();
            CalculateTotals();
        }
    }

    public double Imponibile { get => _imponibile; private set { _imponibile = value; OnPropertyChanged(); } }
    public double IvaTotale { get => _ivaTotale; private set { _ivaTotale = value; OnPropertyChanged(); } }
    public double TotaleGenerale { get => _totaleGenerale; private set { _totaleGenerale = value; OnPropertyChanged(); } }

    public bool IsJointVenture
    {
        get => _isJointVenture;
        set
        {
            _isJointVenture = value;
            OnPropertyChanged();
            if (!value)
            {
                PartnerCompanyName = string.Empty;
                OurCosts.Clear();
                PartnerCosts.Clear();
                AdditionalCosts.Clear();
            }
        }
    }
    #endregion

    #region Document Logic
    public void ResetQuote()
    {
        Materials.Clear();
        Labors.Clear();
        AttachedImages.Clear();
        OurCosts.Clear();
        PartnerCosts.Clear();
        AdditionalCosts.Clear();

        _selectedCustomer = null;
        _selectedSecondCustomer = null;
        _isSecondCustomerEnabled = false;
        _isJointVenture = false;
        _partnerCompanyName = string.Empty;

        _isEditingExistingQuote = false;

        _materialDiscount = 0;
        _laborDiscount = 0;
        _ivaType = string.Empty;

        PaymentTerms = _companyData.Termini_pagamento;
        QuoteNumber = _companyData.Counter.ToString();

        OnPropertyChanged(nameof(SelectedCustomer));
        OnPropertyChanged(nameof(SelectedSecondCustomer));
        OnPropertyChanged(nameof(IsSecondCustomerEnabled));
        OnPropertyChanged(nameof(MaterialDiscount));
        OnPropertyChanged(nameof(LaborDiscount));
        OnPropertyChanged(nameof(IvaType));
        OnPropertyChanged(nameof(IsJointVenture));
        OnPropertyChanged(nameof(PartnerCompanyName));

        ResetInputs();
        CalculateTotals();
    }

    public async Task<bool> HasMissingPdfsAsync()
    {
        try
        {
            // Carica solo i summary (leggeri) invece dell'intero storico
            var summaries = await _dataService.GetQuoteSummariesAsync(int.MaxValue);
            return summaries.Any(e => !string.IsNullOrWhiteSpace(e.PdfPath) && !File.Exists(e.PdfPath));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HasMissingPdfs] Errore: {ex.Message}");
            return false;
        }
    }

    public async Task GenerateInitialPdfsAsync(IProgress<string>? progress = null)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            string storageRoot = _storagePathService.GetPdfRootPath();
            if (!Directory.Exists(storageRoot))
            {
                progress?.Report("Cartella di rete non disponibile - generazione PDF saltata.");
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[STARTUP][HISTORY] Errore verifica cartella: {ex.Message}");
            progress?.Report("Cartella di rete non configurata - generazione PDF saltata.");
            return;
        }

        progress?.Report("Analisi storico: caricamento elenco preventivi...");
        var historyEntries = await _quoteHistoryService.LoadAsync();

        int total = historyEntries.Count;
        int current = 0;

        foreach (var entry in historyEntries)
        {
            current++;
            progress?.Report($"Analisi storico: {current}/{total} - Preventivo {entry.QuoteNumber}");

            try
            {
                if (string.IsNullOrWhiteSpace(entry.CustomerName) || !HasMatchingCustomer(entry.CustomerName))
                    continue;

                string existingPath = _quoteHistoryService.EnsurePdfExists(entry);
                if (!string.IsNullOrWhiteSpace(existingPath) && File.Exists(existingPath))
                    continue;

                progress?.Report($"Analisi storico: {current}/{total} - Generazione PDF {entry.QuoteNumber}");

                var pdfContext = new PdfGenerationContext
                {
                    QuoteNumber = entry.QuoteNumber,
                    Date = entry.Date,
                    PaymentTerms = entry.PaymentTerms,
                    IvaType = string.IsNullOrWhiteSpace(entry.IvaType) ? "esclusa" : entry.IvaType,
                    CustomerName = entry.CustomerName,
                    ReferenceName = entry.ReferenceName,
                    SelectedLogo = _selectedLogo,
                    MaterialDiscount = entry.MaterialDiscount,
                    LaborDiscount = entry.LaborDiscount,
                    Materials = entry.Materials,
                    Labors = entry.Labors,
                    Imponibile = entry.Imponibile,
                    Total = entry.Total,
                    Attachments = entry.Attachments,
                    AllCustomers = _allCustomers
                };

                await Task.Run(() => { GeneratePdfFromContext(pdfContext, entry.PdfPath); });

                Debug.WriteLine($"[STARTUP][HISTORY] PDF generato per {entry.QuoteNumber} in {watch.Elapsed}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[STARTUP][HISTORY] ERRORE su preventivo {entry.QuoteNumber}: {ex.Message}");
            }
        }

        progress?.Report("Analisi PDF storico completata.");
        Debug.WriteLine($"[STARTUP][HISTORY] Analisi completa in {watch.Elapsed}");
    }

    private void GeneratePdfFromContext(PdfGenerationContext ctx, string targetPath)
    {
        try
        {
            string tempRoot = App.AppSettings.App.GetEffectiveTempPath();
            Directory.CreateDirectory(tempRoot);
            string tempPath = Path.Combine(tempRoot, Path.GetFileName(targetPath));

            _pdfService.GenerateQuoteFromContext(ctx, _companyData, tempPath);

            string? targetFolder = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetFolder))
                Directory.CreateDirectory(targetFolder);

            File.Copy(tempPath, targetPath, overwrite: true);
            File.Delete(tempPath);

            Debug.WriteLine($"[PDF] Generato: {targetPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PDF] Errore generazione {targetPath}: {ex.Message}");
        }
    }

    public List<QuoteHistoryEntry> GetHistoryEntriesWithoutMatchingCustomer(IEnumerable<QuoteHistoryEntry> historyEntries)
    {
        return historyEntries
            .Where(entry => !HasMatchingCustomer(entry.CustomerName))
            .ToList();
    }

    private bool HasMatchingCustomer(string? customerName)
    {
        if (string.IsNullOrWhiteSpace(customerName))
            return false;

        string normalizedEntryName = NormalizeMatchText(customerName);
        return AllCustomers.Any(customer =>
            NormalizeMatchText(customer.BusinessName) == normalizedEntryName);
    }

    private static string NormalizeMatchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().Replace("_", " ").ToUpperInvariant();
    }

    public void GeneratePdf() => _ = GeneratePdfAsync(!_isEditingExistingQuote);

    public async Task GeneratePdfAsync(
        bool incrementCounter = true,
        bool openAfterGeneration = true,
        bool generateViaTempFile = false,
        DateTime? specificDate = null,
        string? forceTargetPath = null)
    {
        if (!App.AppSettings.App.GeneratePDF)
        {
            MessageBox.Show("La generazione PDF è disabilitata nelle impostazioni.");
            return;
        }
        if (SelectedCustomer == null)
        {
            MessageBox.Show("Seleziona un cliente prima di generare il PDF.");
            return;
        }

        if (incrementCounter && !_isEditingExistingQuote)
        {
            try
            {
                int nextNumber = await _dataService.GetNextQuoteNumberAsync();
                _companyData.Counter = nextNumber;
                QuoteNumber = nextNumber.ToString();
                OnPropertyChanged(nameof(QuoteNumber));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Impossibile ottenere il prossimo numero preventivo dal database.\n\n{ex.Message}",
                    "Errore numerazione", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        DateTime effectiveDate = specificDate ?? DateTime.Now;

        string targetPath = !string.IsNullOrWhiteSpace(forceTargetPath)
            ? forceTargetPath
            : _storagePathService.BuildQuotePdfPath(
                SelectedCustomer.BusinessName,
                QuoteNumber,
                effectiveDate,
                IsSecondCustomerEnabled ? SelectedSecondCustomer?.BusinessName : null);

        string pathToGenerate = targetPath;

        if (generateViaTempFile)
        {
            string tempRoot = App.AppSettings.App.GetEffectiveTempPath();
            Directory.CreateDirectory(tempRoot);
            string tempFileName = Path.GetFileName(targetPath);
            pathToGenerate = Path.Combine(tempRoot, tempFileName);
            Debug.WriteLine($"Generazione temporanea PDF: {pathToGenerate}");
        }
        else
        {
            string? targetFolder = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetFolder))
                Directory.CreateDirectory(targetFolder);
        }

        _pdfService.GenerateQuote(this, _companyData, pathToGenerate);

        if (generateViaTempFile)
        {
            try
            {
                string? targetFolder = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetFolder))
                    Directory.CreateDirectory(targetFolder);

                File.Copy(pathToGenerate, targetPath, overwrite: true);
                Debug.WriteLine($"PDF copiato su destinazione: {targetPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERRORE copia PDF su share: {targetPath} -> {ex.Message}");
            }

            try { File.Delete(pathToGenerate); }
            catch (Exception ex) { Debug.WriteLine($"[GENERATEPDF] Error: {ex.Message}"); }
        }

        SaveToHistory(targetPath, incrementCounter);

        if (!openAfterGeneration) return;

        try { Process.Start("explorer.exe", $"/select,\"{targetPath}\""); }
        catch { Process.Start("explorer.exe", targetPath); }
    }

    public void LoadQuoteFromHistory(QuoteHistoryEntry entry, bool isEdit = false)
    {
        ResetQuote();

        SelectedCustomer = AllCustomers.FirstOrDefault(c => c.BusinessName == entry.CustomerName);
        CustomerBorderBrush = _selectedCustomer != null ? Brushes.Green : Brushes.Red;
        OnPropertyChanged(nameof(SelectedCustomer));

        if (!string.IsNullOrWhiteSpace(entry.ReferenceName))
        {
            IsSecondCustomerEnabled = true;
            SelectedSecondCustomer = AllCustomers.FirstOrDefault(c => c.BusinessName == entry.ReferenceName);
        }

        PaymentTerms = entry.PaymentTerms;
        IvaType = !string.IsNullOrEmpty(entry.IvaType) ? entry.IvaType : "esclusa";

        _materialDiscount = entry.MaterialDiscount;
        _laborDiscount = entry.LaborDiscount;
        OnPropertyChanged(nameof(MaterialDiscount));
        OnPropertyChanged(nameof(LaborDiscount));

        _isEditingExistingQuote = isEdit;
        if (isEdit)
            QuoteNumber = entry.QuoteNumber;

        // Collaborazione
        _isJointVenture = entry.IsJointVenture;
        _partnerCompanyName = entry.PartnerCompanyName;
        OnPropertyChanged(nameof(IsJointVenture));
        OnPropertyChanged(nameof(PartnerCompanyName));

        OurCosts.Clear();
        foreach (var c in entry.OurCosts) OurCosts.Add(c);
        PartnerCosts.Clear();
        foreach (var c in entry.PartnerCosts) PartnerCosts.Add(c);
        AdditionalCosts.Clear();
        foreach (var c in entry.AdditionalCosts) AdditionalCosts.Add(c);

        foreach (var m in entry.Materials)
        {
            Materials.Add(new Item
            {
                Name = m.Name,
                Description = m.Description,
                UnitPrice = m.UnitPrice,
                Quantity = m.Quantity,
                Discount = m.Discount,
                IsSignificant = m.IsSignificant,
                SortOrder = m.SortOrder
            });
        }

        foreach (var l in entry.Labors)
        {
            Labors.Add(new Item
            {
                Name = l.Name,
                Description = l.Description,
                UnitPrice = l.UnitPrice,
                Quantity = l.Quantity,
                Discount = l.Discount,
                IsSignificant = l.IsSignificant,
                SortOrder = l.SortOrder
            });
        }

        foreach (var attachment in entry.Attachments)
        {
            AttachedImages.Add(new SelectedAttachment
            {
                FileName = attachment.FileName,
                FilePath = string.Empty,
                ContentType = attachment.ContentType,
                Content = attachment.Content
            });
        }

        UpdateItemSortOrders();
        CalculateTotals();
    }

    public string EnsurePdfExists(QuoteHistoryEntry entry) => _quoteHistoryService.EnsurePdfExists(entry);

    public void GenerateCostsPdf()
    {
        if (!IsJointVenture)
        {
            MessageBox.Show("La collaborazione non è attiva.", "Avviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (SelectedCustomer == null)
        {
            MessageBox.Show("Seleziona un cliente prima di generare il PDF dei costi.", "Avviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            string basePath = _storagePathService.BuildQuotePdfPath(
                SelectedCustomer.BusinessName,
                QuoteNumber,
                DateTime.Now,
                IsSecondCustomerEnabled ? SelectedSecondCustomer?.BusinessName : null);

            string costsPath = Path.Combine(
                Path.GetDirectoryName(basePath) ?? string.Empty,
                Path.GetFileNameWithoutExtension(basePath) + "_COSTI.pdf");

            string? folder = Path.GetDirectoryName(costsPath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            _pdfService.GenerateCostsPdf(new CostsPdfContext
            {
                QuoteNumber = QuoteNumber,
                Date = DateTime.Now,
                CustomerName = SelectedCustomer.BusinessName,
                PartnerCompanyName = PartnerCompanyName,
                OurCosts = OurCosts.ToList(),
                PartnerCosts = PartnerCosts.ToList(),
                AdditionalCosts = AdditionalCosts.ToList(),
                Imponibile = Imponibile,
                Total = TotaleGenerale
            }, _companyData, costsPath);

            Process.Start("explorer.exe", $"/select,\"{costsPath}\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore generazione PDF costi:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    #endregion

    #region Attachments
    public void AddAttachmentFromPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        if (AttachedImages.Any(x => x.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
            return;

        AttachedImages.Add(new SelectedAttachment
        {
            FileName = Path.GetFileName(filePath),
            FilePath = filePath,
            ContentType = GetContentType(filePath),
            Content = File.ReadAllBytes(filePath)
        });
    }

    public void RemoveAttachment(SelectedAttachment attachment) => AttachedImages.Remove(attachment);

    private static string GetContentType(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }
    #endregion

    #region History
    public void LoadHistory() => _ = LoadHistoryAsync();
    public void SaveHistory() => _ = SaveHistoryAsync();
    public void LoadHistory(int takeCount) => _ = LoadHistoryAsync(takeCount);

    public async Task LoadHistorySummariesAsync(int count, CancellationToken cancellationToken = default)
    {
        try
        {
            var service = new QuoteHistoryService(App.DataService, StoragePathService.Instance);
            var summaries = await service.LoadTopSummariesAsync(count);

            if (cancellationToken.IsCancellationRequested)
                return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                HistorySummaries.Clear();
                foreach (var s in summaries)
                    HistorySummaries.Add(s);
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoadHistorySummaries] Error: {ex.Message}");
        }
    }

    public async Task<List<QuoteHistorySummary>> SearchHistorySummariesAsync(string searchText, int take, CancellationToken cancellationToken = default)
    {
        try
        {
            var service = new QuoteHistoryService(App.DataService, StoragePathService.Instance);
            var results = await service.SearchSummariesAsync(searchText, take);

            if (cancellationToken.IsCancellationRequested)
                return new List<QuoteHistorySummary>();

            return results;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SearchHistorySummaries] Error: {ex.Message}");
            return new List<QuoteHistorySummary>();
        }
    }

    private async Task LoadHistoryAsync(int? takeCount = null)
    {
        try
        {
            List<QuoteHistoryEntry> data;

            if (takeCount.HasValue && takeCount.Value > 0)
                data = await _quoteHistoryService.LoadTopAsync(takeCount.Value);
            else
                data = await _quoteHistoryService.LoadAsync();

            History.Clear();
            foreach (var entry in data)
                History.Add(entry);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante il caricamento dello storico.\n\n{ex.Message}",
                "Errore caricamento storico", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static byte[] ReadPdfBytesWithRetry(string pdfPath, int maxAttempts = 3, int delayMs = 200)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                if (File.Exists(pdfPath))
                    return File.ReadAllBytes(pdfPath);
            }
            catch (IOException)
            {
                Debug.WriteLine($"[ReadPdf] Tentativo {i + 1}/{maxAttempts} fallito per: {pdfPath}");
            }

            if (i < maxAttempts - 1)
                System.Threading.Thread.Sleep(delayMs);
        }

        return [];
    }

    private void SaveToHistory(string pdfPath, bool isNewEntry = false)
    {
        byte[] pdfBytes = ReadPdfBytesWithRetry(pdfPath);

        var entry = new QuoteHistoryEntry
        {
            QuoteNumber = QuoteNumber,
            Date = DateTime.Now,
            CustomerName = SelectedCustomer?.BusinessName ?? "Sconosciuto",
            ReferenceName = IsSecondCustomerEnabled ? (SelectedSecondCustomer?.BusinessName ?? "") : "",
            PdfPath = pdfPath,
            PaymentTerms = PaymentTerms,
            IvaType = IvaType,
            Notes = string.Empty,
            Materials = Materials.ToList(),
            Labors = Labors.ToList(),
            Imponibile = Imponibile,
            MaterialDiscount = MaterialDiscount,
            LaborDiscount = LaborDiscount,
            Total = TotaleGenerale,
            Status = QuoteStatus.Finalizzato,
            LastModifiedUtc = DateTime.UtcNow,
            IsJointVenture = IsJointVenture,
            PartnerCompanyName = PartnerCompanyName,
            OurCosts = OurCosts.ToList(),
            PartnerCosts = PartnerCosts.ToList(),
            AdditionalCosts = AdditionalCosts.ToList(),
            PdfFile = pdfBytes.Length == 0 ? null : new StoredFile
            {
                FileName = Path.GetFileName(pdfPath) ?? "preventivo.pdf",
                ContentType = "application/pdf",
                Content = pdfBytes,
                ImportedAt = DateTime.Now
            },
            Attachments = AttachedImages.Select(a => new StoredFile
            {
                FileName = a.FileName,
                ContentType = a.ContentType,
                Content = a.Content,
                ImportedAt = DateTime.Now
            }).ToList()
        };

        if (isNewEntry)
        {
            History.Insert(0, entry);
            _ = SaveSingleHistoryEntrySafeAsync(entry);
        }
        else
        {
            var existingEntry = History.FirstOrDefault(x => x.QuoteNumber == QuoteNumber);
            if (existingEntry != null)
            {
                entry.Date = existingEntry.Date;
                entry.Status = existingEntry.Status;
                entry.Notes = existingEntry.Notes;

                existingEntry.CustomerName = entry.CustomerName;
                existingEntry.ReferenceName = entry.ReferenceName;
                existingEntry.PdfPath = entry.PdfPath;
                existingEntry.PaymentTerms = entry.PaymentTerms;
                existingEntry.IvaType = entry.IvaType;
                existingEntry.Notes = entry.Notes;
                existingEntry.Materials = entry.Materials;
                existingEntry.Labors = entry.Labors;
                existingEntry.Imponibile = entry.Imponibile;
                existingEntry.MaterialDiscount = entry.MaterialDiscount;
                existingEntry.LaborDiscount = entry.LaborDiscount;
                existingEntry.Total = entry.Total;
                existingEntry.PdfFile = entry.PdfFile;
                existingEntry.Attachments = entry.Attachments;
                existingEntry.IsJointVenture = entry.IsJointVenture;
                existingEntry.PartnerCompanyName = entry.PartnerCompanyName;
                existingEntry.OurCosts = entry.OurCosts;
                existingEntry.PartnerCosts = entry.PartnerCosts;
                existingEntry.AdditionalCosts = entry.AdditionalCosts;

                _ = SaveSingleHistoryEntrySafeAsync(entry);
            }
            else
            {
                // Preventivo non in memoria (altro PC): recupera la data originale dal DB
                _ = SaveWithOriginalDateAsync(entry);
            }
        }
    }
    
    private async Task SaveWithOriginalDateAsync(QuoteHistoryEntry entry)
    {
        try
        {
            var existing = await _dataService.GetQuoteByNumberAsync(entry.QuoteNumber);
            if (existing != null)
                entry.Date = existing.Date;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SaveWithOriginalDate] Errore recupero data originale: {ex.Message}");
        }

        History.Insert(0, entry);
        await SaveSingleHistoryEntrySafeAsync(entry);
    }
    
    private async Task SaveSingleHistoryEntrySafeAsync(QuoteHistoryEntry entry)
    {
        int attempts = 0;
        while (_isSavingQuoteHistory && attempts < 10)
        {
            await Task.Delay(200);
            attempts++;
        }

        if (_isSavingQuoteHistory)
        {
            Debug.WriteLine("[SAVE HISTORY] Salvataggio già in corso dopo attesa, skip.");
            return;
        }

        _isSavingQuoteHistory = true;

        try
        {
            var entryToSave = entry;
            if (entry.PdfFile?.Content?.Length > 2 * 1024 * 1024)
                entryToSave = CloneEntryWithoutPdfBytes(entry);

            await _dataService.SaveQuoteAsync(entryToSave);
        }
        catch (System.Text.Json.JsonException jsonEx)
        {
            Debug.WriteLine($"[SAVE HISTORY] Errore JSON: {jsonEx.Message}");
            try
            {
                var fallback = CloneEntryWithoutPdfBytes(entry);
                await _dataService.SaveQuoteAsync(fallback);
            }
            catch (Exception retryEx)
            {
                MessageBox.Show($"Errore durante il salvataggio dello storico.\n\n{retryEx.Message}",
                    "Errore salvataggio storico", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante il salvataggio dello storico.\n\n{ex.Message}",
                "Errore salvataggio storico", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isSavingQuoteHistory = false;
        }
    }

    private static QuoteHistoryEntry CloneEntryWithoutPdfBytes(QuoteHistoryEntry entry)
    {
        return new QuoteHistoryEntry
        {
            QuoteNumber = entry.QuoteNumber,
            Date = entry.Date,
            CustomerName = entry.CustomerName,
            ReferenceName = entry.ReferenceName,
            PdfPath = entry.PdfPath,
            PaymentTerms = entry.PaymentTerms,
            IvaType = entry.IvaType,
            Notes = entry.Notes,
            Materials = entry.Materials,
            Labors = entry.Labors,
            Imponibile = entry.Imponibile,
            MaterialDiscount = entry.MaterialDiscount,
            LaborDiscount = entry.LaborDiscount,
            Total = entry.Total,
            Status = entry.Status,
            LastModifiedUtc = entry.LastModifiedUtc,
            SyncHash = entry.SyncHash,
            IsJointVenture = entry.IsJointVenture,
            PartnerCompanyName = entry.PartnerCompanyName,
            OurCosts = entry.OurCosts,
            PartnerCosts = entry.PartnerCosts,
            AdditionalCosts = entry.AdditionalCosts,
            PdfFile = entry.PdfFile == null ? null : new StoredFile
            {
                FileName = entry.PdfFile.FileName,
                ContentType = entry.PdfFile.ContentType,
                Content = [],
                ImportedAt = entry.PdfFile.ImportedAt
            },
            Attachments = entry.Attachments.Select(a => new StoredFile
            {
                FileName = a.FileName,
                ContentType = a.ContentType,
                Content = [],
                ImportedAt = a.ImportedAt
            }).ToList()
        };
    }

    private async Task SaveHistoryAsync()
    {
        if (_isSavingHistory)
            return;

        try
        {
            _isSavingHistory = true;
            var snapshot = History.ToList();
            await _quoteHistoryService.SaveAsync(snapshot);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante il salvataggio dello storico.\n\n{ex.Message}",
                "Errore salvataggio storico", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isSavingHistory = false;
        }
    }
    #endregion

    #region Data Loading & Saving
    private async Task LoadDataAsync()
    {
        try
        {
            string assetsPath = GetAssetsPath();
            LoadSignificantMaterialsConfig(assetsPath);

            var company = await _dataService.GetCompanyAsync();
            var customers = await _dataService.GetCustomersAsync();
            var labors = await _dataService.GetLaborCatalogAsync();
            var personalMaterials = await _dataService.GetPersonalMaterialsAsync();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (company != null)
                {
                    _companyData = company;
                    QuoteNumber = _companyData.Counter.ToString();
                    PaymentTerms = _companyData.Termini_pagamento;

                    Debug.WriteLine($"Loghi caricati: {string.Join(" | ", _companyData.Logo)}");
                    Logos.Clear();
                    foreach (var l in _companyData.Logo)
                        Logos.Add(Path.GetFileName(l));

                    if (_companyData.Logo_index < Logos.Count)
                    {
                        _selectedLogo = Logos[_companyData.Logo_index];
                        OnPropertyChanged(nameof(SelectedLogo));
                    }
                }

                _allCustomers = customers.ToList();
                AllCustomers.Clear();
                foreach (var customer in _allCustomers)
                    AllCustomers.Add(customer);

                FilteredSecondCustomers.Clear();
                foreach (var customer in _allCustomers)
                    FilteredSecondCustomers.Add(customer);

                _allCatalogLabors = labors.ToList();
                AllCatalogLabors.Clear();
                foreach (var labor in _allCatalogLabors)
                    AllCatalogLabors.Add(labor);

                _personalMaterials = personalMaterials;
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show("Errore durante il caricamento dati: " + ex.Message);
            });
        }
    }

    public async void SaveCustomersJson()
    {
        try { await SaveCustomersAsync(); }
        catch (Exception ex) { Debug.WriteLine($"[SaveCustomersJson] Error: {ex.Message}"); }
    }

    public void SaveLaborsJson() => _ = SaveLaborsAsync();

    private async Task SaveCustomersAsync()
    {
        try
        {
            _allCustomers = AllCustomers.ToList();
            ApplySecondCustomerFilter(_secondCustomerSearchText);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SaveCustomersAsync] Error: {ex.Message}");
        }
    }

    private async Task SaveLaborsAsync()
    {
        try
        {
            await _dataService.SaveLaborCatalogAsync(AllCatalogLabors);
            _allCatalogLabors = AllCatalogLabors.ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SaveLaborsAsync] Error: {ex.Message}");
        }
    }

    private void SaveCompanyData() => _ = SaveCompanyDataAsync();

    private async Task SaveCompanyDataAsync()
    {
        try
        {
            _companyData.Logo_index = Math.Max(0, Logos.IndexOf(SelectedLogo));
            if (int.TryParse(QuoteNumber, out int counter))
                _companyData.Counter = counter;

            Debug.WriteLine($"[SAVE COMPANY] Logos in memoria: {string.Join(" | ", Logos)}");
            Debug.WriteLine($"[SAVE COMPANY] SelectedLogo: {SelectedLogo}");
            Debug.WriteLine($"[SAVE COMPANY] _companyData.Logo prima del save: {string.Join(" | ", _companyData.Logo)}");

            await _dataService.SaveCompanyAsync(_companyData, SelectedLogo);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SAVE COMPANY] Error: {ex.Message}");
        }
    }

    private void SavePersonalMaterials() => _ = SavePersonalMaterialsAsync();

    private async Task SavePersonalMaterialsAsync()
    {
        try { await _dataService.SavePersonalMaterialsAsync(_personalMaterials); }
        catch (Exception ex) { Debug.WriteLine($"[SAVE PERSONAL MATERIALS] Error: {ex.Message}"); }
    }

    public void AddNewCustomer(Customer c) => _ = AddNewCustomerAsync(c);

    private async Task AddNewCustomerAsync(Customer c)
    {
        try
        {
            var savedCustomer = await _dataService.AddCustomerAsync(c);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                AllCustomers.Add(savedCustomer);
                _allCustomers.Add(savedCustomer);
                ApplyCustomerFilter(_customerSearchText);
                ApplySecondCustomerFilter(_secondCustomerSearchText);
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante il salvataggio del cliente.\n\n{ex.Message}",
                "Errore salvataggio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void UpdateCustomer(Customer updated)
    {
        var existing = AllCustomers.FirstOrDefault(c => ReferenceEquals(c, updated))
            ?? AllCustomers.FirstOrDefault(c =>
                c.BusinessName.Equals(updated.BusinessName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.BusinessName = updated.BusinessName;
            existing.Address = updated.Address;
            existing.Email = updated.Email;
            existing.Phone = updated.Phone;
            existing.MaterialDiscount = updated.MaterialDiscount;
            existing.LaborDiscount = updated.LaborDiscount;
        }

        var existingInAll = _allCustomers.FirstOrDefault(c => ReferenceEquals(c, updated))
            ?? _allCustomers.FirstOrDefault(c =>
                c.BusinessName.Equals(updated.BusinessName, StringComparison.OrdinalIgnoreCase));

        if (existingInAll != null)
        {
            existingInAll.BusinessName = updated.BusinessName;
            existingInAll.Address = updated.Address;
            existingInAll.Email = updated.Email;
            existingInAll.Phone = updated.Phone;
            existingInAll.MaterialDiscount = updated.MaterialDiscount;
            existingInAll.LaborDiscount = updated.LaborDiscount;
        }

        OnPropertyChanged(nameof(SelectedCustomer));
        OnPropertyChanged(nameof(SelectedSecondCustomer));
        _ = UpdateCustomerSafeAsync(updated);
    }

    private async Task UpdateCustomerSafeAsync(Customer updated)
    {
        try { await _dataService.AddCustomerAsync(updated); }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante il salvataggio del cliente.\n\n{ex.Message}",
                "Errore salvataggio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void IncrementCounter() => _ = IncrementCounterAsync();

    private async Task IncrementCounterAsync()
    {
        int nextQuoteNumber = await _dataService.GetNextQuoteNumberAsync();
        _companyData.Counter = nextQuoteNumber;
        QuoteNumber = nextQuoteNumber.ToString();
        OnPropertyChanged(nameof(QuoteNumber));
    }
    #endregion

    #region Filters & Search
    public void ApplyCustomerFilter(string text)
    {
        _customerSearchText = text;
        FilteredCustomers.Clear();
        foreach (var c in AllCustomers.Where(c => c.ContainsText(text)))
            FilteredCustomers.Add(c);
    }

    public void DeleteCustomer(Customer customer) => _ = DeleteCustomerAsync(customer);

    public async Task DeleteCustomerAsync(Customer customer)
    {
        try
        {
            await _dataService.DeleteCustomerAsync(customer.BusinessName);
            AllCustomers.Remove(customer);
            _allCustomers.Remove(customer);
            ApplyCustomerFilter(_customerSearchText);
            ApplySecondCustomerFilter(_secondCustomerSearchText);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossibile eliminare il cliente.\n\n{ex.Message}",
                "Errore eliminazione", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ApplySecondCustomerFilter(string text)
    {
        _secondCustomerSearchText = text;
        FilteredSecondCustomers.Clear();
        foreach (var c in AllCustomers.Where(c => c.ContainsText(text)))
            FilteredSecondCustomers.Add(c);
    }

    public void ApplyLaborFilter(string text)
    {
        _laborSearchText = text;
        FilteredLabors.Clear();
        foreach (var l in AllCatalogLabors.Where(l =>
                     string.IsNullOrWhiteSpace(text) || l.Name.Contains(text, StringComparison.OrdinalIgnoreCase)))
            FilteredLabors.Add(l);
    }

    public void ApplyMaterialFilter(string text) => _ = ApplyMaterialFilterAsync(text);

    public async Task ApplyMaterialFilterAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 3)
        {
            Application.Current.Dispatcher.Invoke(() => AllCatalogMaterials.Clear());
            return;
        }

        try
        {
            var personalMatches = _personalMaterials
                .Where(m => m.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                .Select(p => new VeluxResult
                {
                    Id = "LOCAL_" + p.Name,
                    Label = $"[Locale] {p.Name} - € {p.UnitPrice:N2}",
                    Value = p.Name
                })
                .ToList();

            Application.Current.Dispatcher.Invoke(() =>
            {
                AllCatalogMaterials.Clear();
                foreach (var p in personalMatches)
                    AllCatalogMaterials.Add(p);
            });

            var veluxResults = await _veluxService.SearchProductsAsync(text);
            Debug.WriteLine($"[SEARCH] Velux: {veluxResults.Count} | Locali: {personalMatches.Count}");

            Application.Current.Dispatcher.Invoke(() =>
            {
                for (int i = 0; i < veluxResults.Count; i++)
                    AllCatalogMaterials.Insert(i, veluxResults[i]);
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ApplyMaterialFilter] Errore: {ex.Message}");
        }
    }
    #endregion

    #region Material & Labor Input
    public void AddPersonalMaterial(Item item)
    {
        _personalMaterials.Add(item);
        SavePersonalMaterials();
    }

    public void RemovePersonalMaterial(Item item)
    {
        _personalMaterials.Remove(item);
        SavePersonalMaterials();
    }

    public void SavePersonalMaterialsPublic() => SavePersonalMaterials();

    public void AddMaterial()
    {
        if (string.IsNullOrWhiteSpace(InputName))
            return;

        var newItem = new Item
        {
            Name = InputName,
            Description = InputDescription,
            UnitPrice = InputValue,
            Quantity = InputQuantity,
            IsSignificant = IsSignificant,
            SortOrder = Materials.Count
        };

        Materials.Add(newItem);

        var existing = _personalMaterials.FirstOrDefault(m =>
            m.Name.Equals(newItem.Name, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.Description = newItem.Description;
            existing.UnitPrice = newItem.UnitPrice;
            existing.IsSignificant = newItem.IsSignificant;
        }
        else
        {
            _personalMaterials.Add(new Item
            {
                Name = newItem.Name,
                Description = newItem.Description,
                UnitPrice = newItem.UnitPrice,
                Quantity = 1,
                IsSignificant = newItem.IsSignificant,
                SortOrder = _personalMaterials.Count
            });
        }

        SavePersonalMaterials();
        ResetInputs();
    }

    public void AddLabor()
    {
        if (string.IsNullOrWhiteSpace(InputName))
            return;

        Labors.Add(new Item
        {
            Name = InputName,
            Description = InputDescription,
            UnitPrice = InputValue,
            Quantity = InputQuantity,
            IsSignificant = IsSignificant,
            SortOrder = Labors.Count
        });

        ResetInputs();
    }

    private void ResetInputs()
    {
        InputName = "";
        InputDescription = "";
        InputValue = 0;
        SelectedCatalogLabor = null;
        SelectedCatalogMaterial = null;
        OnPropertyChanged(string.Empty);
    }

    public async Task FetchVeluxDetails(string uuid)
    {
        if (string.IsNullOrEmpty(uuid))
            return;

        if (uuid.StartsWith("LOCAL_"))
        {
            string nameToFind = uuid.Substring(6);
            var localItem = _personalMaterials.FirstOrDefault(m => m.Name == nameToFind);

            if (localItem != null)
            {
                InputName = localItem.Name;
                InputDescription = localItem.Description;
                InputValue = localItem.UnitPrice;
                InputQuantity = 1;
                IsSignificant = localItem.IsSignificant;

                OnPropertyChanged(nameof(InputName));
                OnPropertyChanged(nameof(InputDescription));
                OnPropertyChanged(nameof(InputValue));
                OnPropertyChanged(nameof(InputQuantity));
                OnPropertyChanged(nameof(IsSignificant));
                return;
            }
        }

        var details = await _veluxService.GetProductDetailsAsync(uuid);
        if (details != null)
        {
            InputName = details.Name;
            InputDescription = details.Description;
            InputValue = details.UnitPrice;
            InputQuantity = 1;
            IsSignificant = IsMaterialSignificant(details.Name);

            OnPropertyChanged(nameof(InputName));
            OnPropertyChanged(nameof(InputDescription));
            OnPropertyChanged(nameof(InputValue));
            OnPropertyChanged(nameof(InputQuantity));
            OnPropertyChanged(nameof(IsSignificant));
        }
    }
    #endregion

    #region Totals & Item Updates
    private void OnItemsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (Item item in e.NewItems)
            {
                item.PropertyChanged += (s, args) =>
                {
                    Debug.WriteLine($"Item changed: {args.PropertyName} = {(s as Item)?.IsSignificant}");
                    CalculateTotals();
                };
            }
        }

        UpdateItemSortOrders();
        CalculateTotals();
    }

    public void UpdateItemSortOrders()
    {
        for (int i = 0; i < Materials.Count; i++)
            Materials[i].SortOrder = i;
        for (int i = 0; i < Labors.Count; i++)
            Labors[i].SortOrder = i;
    }

    private void ApplyCustomerDiscounts(Customer customer)
    {
        MaterialDiscount = customer.MaterialDiscount;
        LaborDiscount = customer.LaborDiscount;
    }

    public void CalculateTotals()
    {
        var totals = _quoteCalculator.Calculate(Materials, Labors, MaterialDiscount, LaborDiscount, IvaType);
        Imponibile = totals.Imponibile;
        IvaTotale = totals.IvaTotale;
        TotaleGenerale = totals.TotaleGenerale;
    }
    #endregion

    #region Paths / Config
    private string GetAssetsPath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "Assets"),
            Path.Combine(baseDir, "assets"),
            Path.Combine(baseDir, "..", "..", "..", "Assets"),
            Path.Combine(baseDir, "..", "..", "..", "assets")
        ];
        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    private void LoadSignificantMaterialsConfig(string assetsPath)
    {
        _significantMaterialPrefixes.Clear();
        string configPath = Path.Combine(assetsPath, "significant_materials.json");
        if (!File.Exists(configPath)) return;

        try
        {
            string json = File.ReadAllText(configPath);
            var prefixes = JsonSerializer.Deserialize<List<string>>(json);
            if (prefixes == null) return;
            foreach (var prefix in prefixes)
                if (!string.IsNullOrWhiteSpace(prefix))
                    _significantMaterialPrefixes.Add(prefix.Trim());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoadSignificantMaterialsConfig] Error: {ex.Message}");
        }
    }

    private bool IsMaterialSignificant(string? materialName)
    {
        if (string.IsNullOrWhiteSpace(materialName)) return false;
        return _significantMaterialPrefixes.Any(prefix =>
            materialName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
    #endregion

    #region UI Actions
    public void OpenCustomerFolder()
    {
        try
        {
            if (SelectedCustomer == null)
            {
                MessageBox.Show("Seleziona un cliente prima di aprire la cartella.");
                return;
            }
            _storagePathService.OpenFolder(_storagePathService.BuildCustomerPdfFolder(SelectedCustomer.BusinessName, null));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossibile aprire la cartella.\n\n{ex.Message}", "Errore apertura", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void OpenReferenceFolder()
    {
        try
        {
            if (SelectedCustomer == null)
            {
                MessageBox.Show("Seleziona un cliente prima di aprire la cartella.");
                return;
            }
            if (!IsSecondCustomerEnabled || SelectedSecondCustomer == null)
            {
                MessageBox.Show("Seleziona anche il riferimento prima di aprire la cartella.");
                return;
            }
            _storagePathService.OpenFolder(_storagePathService.BuildCustomerPdfFolder(
                SelectedCustomer.BusinessName, SelectedSecondCustomer.BusinessName));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossibile aprire la cartella del riferimento.\n\n{ex.Message}", "Errore apertura", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<bool> HandleVeluxLogin()
    {
        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var win = new VeluxLoginWindow { Owner = Application.Current.MainWindow };
            return win.ShowDialog() == true;
        });
    }
    #endregion

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    #endregion
}