using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EdilPaintPreventibiviGen.Models;

public class QuoteHistorySummary : INotifyPropertyChanged
{
    private string _quoteNumber = string.Empty;
    private DateTimeOffset _date;
    private string _customerName = string.Empty;
    private string _referenceName = string.Empty;
    private string _pdfPath = string.Empty;
    private decimal _total;
    private string _ivaType = string.Empty;
    private double _materialDiscount;
    private double _laborDiscount;
    private QuoteStatus _status;
    private string _notes = string.Empty;
    private SyncStatus _syncStatus;
    private bool _isJointVenture;
    private string _partnerCompanyName = string.Empty;
    private string _createdByDevice = string.Empty;
    private string _lastModifiedByDevice = string.Empty;
    private DateTime? _sentAtUtc;
    private string _sentMethod = string.Empty;
    private string _sentRecipient = string.Empty;
    private string _sentByDevice = string.Empty;
    private DateTime? _lastReminderAtUtc;
    private int _reminderCount;
    private string _lastReminderByDevice = string.Empty;

    public string QuoteNumber
    {
        get => _quoteNumber;
        set { _quoteNumber = value; OnPropertyChanged(); }
    }

    public DateTimeOffset Date
    {
        get => _date;
        set { _date = value; OnPropertyChanged(); }
    }

    public string CustomerName
    {
        get => _customerName;
        set { _customerName = value; OnPropertyChanged(); OnPropertyChanged(nameof(CustomerReferenceDisplay)); }
    }

    public string ReferenceName
    {
        get => _referenceName;
        set { _referenceName = value; OnPropertyChanged(); OnPropertyChanged(nameof(CustomerReferenceDisplay)); }
    }

    public string PdfPath
    {
        get => _pdfPath;
        set { _pdfPath = value; OnPropertyChanged(); }
    }

    public decimal Total
    {
        get => _total;
        set { _total = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalDisplay)); }
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
            OnPropertyChanged(nameof(IvaDisplay));
        }
    }

    public double MaterialDiscount
    {
        get => _materialDiscount;
        set
        {
            if (Math.Abs(_materialDiscount - value) < 0.001)
                return;

            _materialDiscount = value;
            OnPropertyChanged();
            OnDiscountChanged();
        }
    }

    public double LaborDiscount
    {
        get => _laborDiscount;
        set
        {
            if (Math.Abs(_laborDiscount - value) < 0.001)
                return;

            _laborDiscount = value;
            OnPropertyChanged();
            OnDiscountChanged();
        }
    }

    public string IvaDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(IvaType))
                return "-";

            string compact = IvaType
                .Trim()
                .Replace(" ", string.Empty)
                .Replace("%", string.Empty)
                .ToUpperInvariant();

            if (compact.Contains("10+22", StringComparison.Ordinal) ||
                compact.Contains("10/22", StringComparison.Ordinal) ||
                (compact.Contains("10", StringComparison.Ordinal) && compact.Contains("22", StringComparison.Ordinal)))
            {
                return "10+22";
            }

            if (compact.Contains("22", StringComparison.Ordinal))
                return "22%";

            if (compact.Contains("10", StringComparison.Ordinal))
                return "10%";

            if (compact.Contains("ESCLUSA", StringComparison.Ordinal) ||
                compact.Contains("NOIVA", StringComparison.Ordinal))
            {
                return "Esclusa";
            }

            return IvaType.Trim();
        }
    }

    public bool HasDiscount => Math.Abs(MaterialDiscount) > 0.001 || Math.Abs(LaborDiscount) > 0.001;

    public string DiscountDisplay
    {
        get
        {
            bool hasMaterialDiscount = Math.Abs(MaterialDiscount) > 0.001;
            bool hasLaborDiscount = Math.Abs(LaborDiscount) > 0.001;

            if (!hasMaterialDiscount && !hasLaborDiscount)
                return "No sconto";

            if (hasMaterialDiscount && hasLaborDiscount && Math.Abs(MaterialDiscount - LaborDiscount) < 0.001)
                return $"Sc. {FormatPercent(MaterialDiscount)}";

            if (hasMaterialDiscount && hasLaborDiscount)
                return $"Sc. M {FormatPercent(MaterialDiscount)} / L {FormatPercent(LaborDiscount)}";

            return hasMaterialDiscount
                ? $"Sc. mat. {FormatPercent(MaterialDiscount)}"
                : $"Sc. lav. {FormatPercent(LaborDiscount)}";
        }
    }

    public string TotalDisplay => $"{Total:N2}";

    public string CreatedDateDisplay => Date.ToLocalTime().ToString("dd/MM/yyyy");

    public string SentDateDisplay => SentAtUtc.HasValue
        ? SentAtUtc.Value.ToLocalTime().ToString("dd/MM/yyyy")
        : "Non inviato";

    public string CustomerReferenceDisplay => string.IsNullOrWhiteSpace(ReferenceName)
        ? CustomerName
        : $"{CustomerName} - Rif. {ReferenceName}";

    public QuoteStatus Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public string Notes
    {
        get => _notes;
        set
        {
            if (_notes == value)
                return;

            _notes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasNotes));
        }
    }

    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    
    public bool IsJointVenture
    {
        get => _isJointVenture;
        set { _isJointVenture = value; OnPropertyChanged(); }
    }

    public string PartnerCompanyName
    {
        get => _partnerCompanyName;
        set { _partnerCompanyName = value; OnPropertyChanged(); }
    }

    public SyncStatus SyncStatus
    {
        get => _syncStatus;
        set { _syncStatus = value; OnPropertyChanged(); }
    }

    public string CreatedByDevice
    {
        get => _createdByDevice;
        set { _createdByDevice = value; OnPropertyChanged(); }
    }

    public string LastModifiedByDevice
    {
        get => _lastModifiedByDevice;
        set { _lastModifiedByDevice = value; OnPropertyChanged(); }
    }

    public DateTime? SentAtUtc
    {
        get => _sentAtUtc;
        set
        {
            _sentAtUtc = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SentDisplay));
            OnPropertyChanged(nameof(SentDateDisplay));
            OnPropertyChanged(nameof(IsSent));
        }
    }

    public string SentMethod
    {
        get => _sentMethod;
        set { _sentMethod = value; OnPropertyChanged(); OnPropertyChanged(nameof(SentDisplay)); }
    }

    public string SentRecipient
    {
        get => _sentRecipient;
        set { _sentRecipient = value; OnPropertyChanged(); }
    }

    public string SentByDevice
    {
        get => _sentByDevice;
        set { _sentByDevice = value; OnPropertyChanged(); }
    }

    public DateTime? LastReminderAtUtc
    {
        get => _lastReminderAtUtc;
        set
        {
            _lastReminderAtUtc = value;
            OnPropertyChanged();
        }
    }

    public int ReminderCount
    {
        get => _reminderCount;
        set { _reminderCount = value; OnPropertyChanged(); }
    }

    public string LastReminderByDevice
    {
        get => _lastReminderByDevice;
        set { _lastReminderByDevice = value; OnPropertyChanged(); }
    }

    public bool IsSent => SentAtUtc.HasValue;

    public string SentDisplay => SentAtUtc.HasValue
        ? $"Inviato il {SentAtUtc.Value.ToLocalTime():dd/MM/yyyy}"
        : "Non inviato";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnDiscountChanged()
    {
        OnPropertyChanged(nameof(HasDiscount));
        OnPropertyChanged(nameof(DiscountDisplay));
    }

    private static string FormatPercent(double value) => $"{value:0.#}%";
}

public enum SyncStatus
{
    LocalOnly,      // Rosso - Solo in JSON locale
    OnlineOnly,     // Giallo - Solo nel database online
    Synced          // Verde - Presente in entrambi
}
