namespace EdilPaintPreventibiviGen.Models;

public sealed class WorkSheetOptions
{
    public IReadOnlyList<string> EmployeeNames { get; init; } = [];
    public DateTime? InterventionDate { get; init; }
    public string AdditionalNotes { get; init; } = string.Empty;
}
