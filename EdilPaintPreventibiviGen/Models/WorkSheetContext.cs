namespace EdilPaintPreventibiviGen.Models;

public sealed record WorkSheetLine(string Name, string Description, decimal Quantity, string UnitOfMeasure = "pz");

public sealed class WorkSheetContext
{
    public string QuoteNumber { get; init; } = string.Empty;
    public DateTime QuoteDate { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.Now;
    public string CustomerName { get; init; } = string.Empty;
    public string ReferenceName { get; init; } = string.Empty;
    public string WorkSite { get; init; } = string.Empty;
    public string ContactPhone { get; init; } = string.Empty;
    public string CustomerNotes { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public IReadOnlyList<string> EmployeeNames { get; init; } = [];
    public DateTime? InterventionDate { get; init; }
    public string AdditionalNotes { get; init; } = string.Empty;
    public string SelectedLogo { get; set; } = string.Empty;
    public bool IsOfflineSnapshot { get; set; }
    public bool MaterialsOrderedByCustomer { get; init; }
    public string SupplierName { get; init; } = string.Empty;
    public string MaterialStatus { get; init; } = string.Empty;
    public DateTime? MaterialOrderDate { get; init; }
    public DateTime? ExpectedDeliveryDate { get; init; }
    public IReadOnlyList<WorkSheetLine> Materials { get; init; } = [];
    public IReadOnlyList<WorkSheetLine> Labors { get; init; } = [];
}
