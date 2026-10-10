namespace EdilPaintPreventibiviGen.Data.Entities;

public sealed class WorkScheduleAssignmentEntity
{
    public Guid EntryId { get; set; }
    public WorkScheduleEntryEntity Entry { get; set; } = null!;
    public Guid EmployeeId { get; set; }
    public EmployeeEntity Employee { get; set; } = null!;
}
