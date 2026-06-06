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
        set { _customerName = value; OnPropertyChanged(); }
    }

    public string ReferenceName
    {
        get => _referenceName;
        set { _referenceName = value; OnPropertyChanged(); }
    }

    public string PdfPath
    {
        get => _pdfPath;
        set { _pdfPath = value; OnPropertyChanged(); }
    }

    public decimal Total
    {
        get => _total;
        set { _total = value; OnPropertyChanged(); }
    }

    public QuoteStatus Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShouldRemind));
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
            OnPropertyChanged(nameof(ReminderDisplay));
            OnPropertyChanged(nameof(ShouldRemind));
        }
    }

    public int ReminderCount
    {
        get => _reminderCount;
        set { _reminderCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReminderDisplay)); }
    }

    public string LastReminderByDevice
    {
        get => _lastReminderByDevice;
        set { _lastReminderByDevice = value; OnPropertyChanged(); }
    }

    public bool IsSent => SentAtUtc.HasValue;

    public bool ShouldRemind =>
        Status == QuoteStatus.Spedito &&
        SentAtUtc.HasValue &&
        DateTime.UtcNow - SentAtUtc.Value.ToUniversalTime() >= TimeSpan.FromDays(7) &&
        (!LastReminderAtUtc.HasValue || DateTime.UtcNow - LastReminderAtUtc.Value.ToUniversalTime() >= TimeSpan.FromDays(7));

    public string SentDisplay => SentAtUtc.HasValue
        ? $"{SentAtUtc.Value.ToLocalTime():dd/MM/yyyy} {SentMethod}".Trim()
        : "Non inviato";

    public string ReminderDisplay => ReminderCount <= 0
        ? "Mai"
        : $"{ReminderCount} solleciti, ultimo {LastReminderAtUtc?.ToLocalTime():dd/MM/yyyy}";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum SyncStatus
{
    LocalOnly,      // Rosso - Solo in JSON locale
    OnlineOnly,     // Giallo - Solo nel database online
    Synced          // Verde - Presente in entrambi
}
