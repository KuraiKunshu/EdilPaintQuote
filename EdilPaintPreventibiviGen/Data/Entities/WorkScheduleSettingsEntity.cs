namespace EdilPaintPreventibiviGen.Data.Entities;

public sealed class WorkScheduleSettingsEntity
{
    public int Id { get; set; } = 1;
    public long Revision { get; set; } = 1;
    public int FullDayStartMinutes { get; set; } = 8 * 60;
    public int FullDayEndMinutes { get; set; } = 17 * 60;
    public int MorningStartMinutes { get; set; } = 8 * 60;
    public int MorningEndMinutes { get; set; } = 12 * 60;
    public int AfternoonStartMinutes { get; set; } = 13 * 60;
    public int AfternoonEndMinutes { get; set; } = 17 * 60;
    public bool UseEmployeeAbbreviations { get; set; } = true;
}
