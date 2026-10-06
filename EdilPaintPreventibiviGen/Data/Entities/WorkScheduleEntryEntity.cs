using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Data.Entities;

public sealed class WorkScheduleEntryEntity
{
    public Guid Id { get; set; }
    public int? QuoteId { get; set; }
    public QuoteEntity? Quote { get; set; }
    public DateTime Date { get; set; }
    public int StartMinutes { get; set; }
    public int EndMinutes { get; set; }
    public WorkScheduleSlotKind SlotKind { get; set; }
    public long SettingsRevision { get; set; }
    public WorkScheduleEntryKind Kind { get; set; }
    public string AbsenceReason { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public WorkScheduleEntryStatus Status { get; set; }
    public long Revision { get; set; } = 1;
    public bool IsDeleted { get; set; }
    public ICollection<WorkScheduleAssignmentEntity> Assignments { get; set; } = [];
}
