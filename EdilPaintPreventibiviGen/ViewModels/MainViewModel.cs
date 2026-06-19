using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using EdilPaintPreventibiviGen.Helpers;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using EdilPaintPreventibiviGen.Views;

namespace EdilPaintPreventibiviGen.ViewModels;

public partial class MainViewModel : INotifyPropertyChanged, IDisposable
{
    #region Services
    private readonly IDataService _dataService;
    private readonly VeluxService _veluxService = new();
    private readonly PdfService _pdfService = new();
    private readonly StoragePathService _storagePathService = StoragePathService.Instance;
    private readonly QuoteCalculator _quoteCalculator = new();
    private readonly QuoteHistoryService _quoteHistoryService;
    private readonly SemaphoreSlim _draftSaveLock = new(1, 1);
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
    private CancellationTokenSource? _veluxDetailsCts;
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
    private bool _isEditingExistingQuote;
    private bool _isGeneratingPdf;
    private bool _isGeneratingCostsPdf;
    private bool _hasPersistedCurrentQuote;
    #endregion

    #region Quote Info
    private string _quoteNumber = "000000";
    private string _selectedLogo = string.Empty;
    private string _paymentTerms = string.Empty;
    private string _partnerCompanyName = string.Empty;
    private DateTime? _loadedQuoteDate;
    private DateTime _loadedQuoteBaseVersionUtc;
    private string _lastSharedDraftContentHash = string.Empty;
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
    private Brush _customerBorderBrush = GetCustomerSelectionBrush(false);
    private Brush _secondCustomerBorderBrush = GetCustomerSelectionBrush(false);
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

    public IEnumerable<QuoteStatus> StatusOptions => Enum
        .GetValues(typeof(QuoteStatus))
        .Cast<QuoteStatus>()
        .Where(status => status != QuoteStatus.DaSollecitare);
    #endregion

    #region Constructor
    public MainViewModel()
    {
        _dataService = App.DataService;
        _quoteHistoryService = new QuoteHistoryService(_dataService, _storagePathService);

        _veluxService.OnLoginRequired += HandleVeluxLogin;

        Materials.CollectionChanged += OnItemsCollectionChanged;
        Labors.CollectionChanged += OnItemsCollectionChanged;
    }

    public void Dispose()
    {
        Materials.CollectionChanged -= OnItemsCollectionChanged;
        Labors.CollectionChanged -= OnItemsCollectionChanged;
        _veluxDetailsCts?.Cancel();
        _veluxDetailsCts?.Dispose();
        _veluxDetailsCts = null;
        _veluxService.OnLoginRequired -= HandleVeluxLogin;
        _veluxService.Dispose();
        _draftSaveLock.Dispose();
    }

    private static Brush GetCustomerSelectionBrush(bool hasCustomer) =>
        ThemeResources.GetBrush(hasCustomer ? "CustomerValidBorderBrush" : "CustomerInvalidBorderBrush");
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
                CustomerBorderBrush = GetCustomerSelectionBrush(true);
            }
            else
            {
                MaterialDiscount = 0;
                LaborDiscount = 0;
                CustomerBorderBrush = GetCustomerSelectionBrush(false);
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
            SecondCustomerBorderBrush = GetCustomerSelectionBrush(value != null);
            OnPropertyChanged();
        }
    }

    public VeluxResult? SelectedCatalogMaterial
    {
        get => _selectedCatalogMaterial;
        set
        {
            _selectedCatalogMaterial = value;
            _veluxDetailsCts?.Cancel();
            _veluxDetailsCts?.Dispose();
            _veluxDetailsCts = null;

            if (value != null)
            {
                _veluxDetailsCts = AppShutdownManager.CreateLinkedTokenSource();
                _ = FetchVeluxDetails(value.Id, _veluxDetailsCts.Token);
            }

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
            if (_selectedLogo == value)
                return;

            _selectedLogo = value ?? string.Empty;
            OnPropertyChanged();
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










    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    #endregion
}
