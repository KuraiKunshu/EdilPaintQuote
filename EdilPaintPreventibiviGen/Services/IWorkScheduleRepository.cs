using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed class WorkScheduleSnapshot
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public WorkScheduleSettings Settings { get; set; } = new();
    public List<WorkScheduleEntry> Entries { get; set; } = [];
    public List<WorkScheduleOrder> Orders { get; set; } = [];
    public List<EmployeeSettingsModel> Employees { get; set; } = [];
    public bool IsCurrent { get; set; }
    public bool HasCachedData { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public sealed record WorkScheduleSettingsSnapshot(WorkScheduleSettings Settings, bool IsCurrent, DateTime? UpdatedAtUtc);

public interface IWorkScheduleRepository
{
    Task<WorkScheduleSnapshot> LoadWorkScheduleAsync(DateTime from, DateTime to, CancellationToken token = default);
    Task<WorkScheduleSettings> GetWorkScheduleSettingsAsync(CancellationToken token = default);
    Task<WorkScheduleSettings> SaveWorkScheduleSettingsAsync(WorkScheduleSettings settings, CancellationToken token = default);
    Task<List<WorkScheduleEntry>> SaveWorkScheduleEntriesAsync(IReadOnlyList<WorkScheduleEntry> entries, CancellationToken token = default);
    Task<WorkScheduleEntry> CompleteWorkScheduleEntryAsync(Guid id, long revision, CancellationToken token = default);
    Task DeleteWorkScheduleEntryAsync(Guid id, long revision, CancellationToken token = default);
}
