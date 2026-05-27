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
        set { _status = value; OnPropertyChanged(); }
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
