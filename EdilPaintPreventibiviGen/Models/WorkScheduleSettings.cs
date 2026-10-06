using System.Globalization;

namespace EdilPaintPreventibiviGen.Models;

public enum WorkScheduleSlotKind { FullDay, Morning, Afternoon, Custom }
public enum WorkScheduleEntryKind { Job, Absence }
public enum WorkScheduleEntryStatus { Planned, Completed }

public sealed class WorkScheduleSettings
{
    public long Revision { get; set; }
    public int FullDayStartMinutes { get; set; } = 8 * 60;
    public int FullDayEndMinutes { get; set; } = 17 * 60;
    public int MorningStartMinutes { get; set; } = 8 * 60;
    public int MorningEndMinutes { get; set; } = 12 * 60;
    public int AfternoonStartMinutes { get; set; } = 13 * 60;
    public int AfternoonEndMinutes { get; set; } = 17 * 60;
    public bool UseEmployeeAbbreviations { get; set; } = true;

    public WorkScheduleSettings CreateValidatedCopy()
    {
        ValidateRange(FullDayStartMinutes, FullDayEndMinutes);
        ValidateRange(MorningStartMinutes, MorningEndMinutes);
        ValidateRange(AfternoonStartMinutes, AfternoonEndMinutes);
        if (MorningStartMinutes < FullDayStartMinutes || AfternoonEndMinutes > FullDayEndMinutes ||
            MorningEndMinutes > AfternoonStartMinutes)
            throw new InvalidOperationException("Mattina e pomeriggio devono rientrare nella giornata intera e non sovrapporsi.");
        return (WorkScheduleSettings)MemberwiseClone();
    }

    public (int Start, int End) GetRange(WorkScheduleSlotKind kind) => kind switch
    {
        WorkScheduleSlotKind.FullDay => (FullDayStartMinutes, FullDayEndMinutes),
        WorkScheduleSlotKind.Morning => (MorningStartMinutes, MorningEndMinutes),
        WorkScheduleSlotKind.Afternoon => (AfternoonStartMinutes, AfternoonEndMinutes),
        _ => throw new InvalidOperationException("Indica l'orario iniziale e finale dell'intervento.")
    };

    public static string FormatTime(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    public static bool TryParseTime(string? text, out int minutes)
    {
        minutes = 0;
        if (!TimeOnly.TryParseExact(text?.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time)) return false;
        minutes = time.Hour * 60 + time.Minute;
        return true;
    }

    public static void ValidateRange(int start, int end)
    {
        if (start < 0 || start >= 24 * 60 || end <= start || end >= 24 * 60)
            throw new InvalidOperationException("Inserisci orari validi tra 00:00 e 23:59. L'ora finale deve essere successiva a quella iniziale.");
    }
}
