using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EdilPaintPreventibiviGen.Models;

public enum PosSheetKind { Work, Equipment }

/// <summary>A POS text, independent of the commercial labor catalog.</summary>
public sealed class PosWorkItem : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Revision { get; set; }
    public bool IsArchived { get; set; }
    public PosSheetKind Kind { get; set; }
    private string _name = "", _description = "", _phases = "", _equipment = "", _risks = "";
    private bool _verified;
    public string Name { get => _name; set => SetText(ref _name, value); }
    public string Description { get => _description; set => SetText(ref _description, value); }
    public string Phases { get => _phases; set => SetText(ref _phases, value); }
    public string Equipment { get => _equipment; set => SetText(ref _equipment, value); }
    public string Risks { get => _risks; set => SetText(ref _risks, value); }
    private string _category = "", _dpi = "", _measures = "", _procedures = "", _organization = "", _coordination = "";
    public string Category { get => _category; set => SetText(ref _category, value); }
    public string Dpi { get => _dpi; set => SetText(ref _dpi, value); }
    public string Measures { get => _measures; set => SetText(ref _measures, value); }
    public string Procedures { get => _procedures; set => SetText(ref _procedures, value); }
    public string Organization { get => _organization; set => SetText(ref _organization, value); }
    public string Coordination { get => _coordination; set => SetText(ref _coordination, value); }
    private List<Guid> _equipmentIds = [];
    public List<Guid> EquipmentIds
    {
        get => _equipmentIds;
        set { var next = value ?? []; if (_equipmentIds.SequenceEqual(next)) return; _equipmentIds = next; Verified = false; Changed(); }
    }
    public bool Verified { get => _verified; set { if (_verified == value) return; _verified = value; Changed(); } }
    private void SetText(ref string field, string? value, [CallerMemberName] string? name = null)
    {
        value ??= "";
        if (field == value) return;
        field = value;
        Verified = false;
        Changed(name);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class PosEmployee
{
    public Guid EmployeeId { get; set; }
    public string Name { get; set; } = "";
    public string Qualification { get; set; } = "";
}

/// <summary>Editable snapshot: template and employee changes never rewrite a saved POS.</summary>
public sealed class PosDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Revision { get; set; }
    public DateTime? SavedAtUtc { get; set; }
    public string QuoteNumber { get; set; } = "";
    public string DocumentRevision { get; set; } = "00";
    public string SiteName { get; set; } = "";
    public string SiteAddress { get; set; } = "";
    public string Client { get; set; } = "";
    public string Contractor { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string CompanyAddress { get; set; } = "";
    public string CompanyVat { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Description { get; set; } = "";
    public string Access { get; set; } = "";
    public string Constraints { get; set; } = "";
    public string Employer { get; set; } = "";
    public string Rspp { get; set; } = "";
    public string Rls { get; set; } = "";
    public string Doctor { get; set; } = "";
    public string Supervisor { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<PosWorkItem> Works { get; set; } = [];
    public List<PosWorkItem> EquipmentSheets { get; set; } = [];
    public List<PosEmployee> Employees { get; set; } = [];
}

public sealed class PosWorkChoice : INotifyPropertyChanged
{
    public PosWorkItem Sheet { get; init; } = new();
    public string Notice { get; init; } = "";
    private bool _selected;
    public bool IsSelected { get => _selected; set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class PosEmployeeChoice : INotifyPropertyChanged
{
    public PosEmployee Employee { get; init; } = new();
    public string Notice { get; init; } = "";
    private bool _selected;
    public bool IsSelected { get => _selected; set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
