using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

internal static class WorkScheduleRules
{
    internal static InvalidOperationException Conflict(Exception? inner = null) => new(
        "L'intervento è stato modificato o eliminato da un altro PC. Ricarica il calendario prima di modificarlo.", inner);

    internal static void ValidateRevision(WorkScheduleEntry requested, WorkScheduleEntry? stored)
    {
        if (stored == null ? requested.Revision != 0 : requested.Revision != stored.Revision)
            throw Conflict();
        if (stored != null && (stored.Kind != requested.Kind ||
            !string.Equals(stored.QuoteNumber, requested.QuoteNumber, StringComparison.Ordinal)))
            throw new InvalidOperationException("Un intervento esistente deve mantenere il proprio ordine. Eliminalo e creane uno nuovo per un ordine diverso.");
    }

    internal static void ValidatePresetTimes(WorkScheduleEntry requested, WorkScheduleEntry? stored,
        WorkScheduleSettings settings)
    {
        if (requested.SlotKind == WorkScheduleSlotKind.Custom) return;
        bool schedulingChanged = stored == null || requested.Date.Date != stored.Date.Date ||
            requested.SlotKind != stored.SlotKind || requested.StartMinutes != stored.StartMinutes ||
            requested.EndMinutes != stored.EndMinutes;
        if (!schedulingChanged) return;
        var range = settings.GetRange(requested.SlotKind);
        if (requested.SettingsRevision != settings.Revision || requested.StartMinutes != range.Start ||
            requested.EndMinutes != range.End)
            throw new InvalidOperationException("Gli orari standard sono stati aggiornati da un altro PC. Ricarica il calendario e scegli nuovamente la fascia oraria.");
    }

    internal static void ValidateOrderAvailabilityForEdit(WorkScheduleEntry requested, WorkScheduleEntry stored,
        DateTime today)
    {
        if (requested.Kind != WorkScheduleEntryKind.Job || requested.IsActiveOrder) return;
        bool currentFuturePlan = stored.Date.Date >= today.Date && stored.Status == WorkScheduleEntryStatus.Planned;
        bool requestedFuturePlan = requested.Date.Date >= today.Date && requested.Status == WorkScheduleEntryStatus.Planned;
        bool scheduleChanged = requested.Date.Date != stored.Date.Date || requested.SlotKind != stored.SlotKind ||
            requested.StartMinutes != stored.StartMinutes || requested.EndMinutes != stored.EndMinutes ||
            !requested.EmployeeIds.ToHashSet().SetEquals(stored.EmployeeIds);
        bool restoringPlan = requestedFuturePlan && stored.Status != WorkScheduleEntryStatus.Planned;
        if ((currentFuturePlan || requestedFuturePlan) && (scheduleChanged || restoringPlan))
            throw new InvalidOperationException($"L'ordine {requested.QuoteNumber} non è più presente nell'elenco Ordini o è stato eliminato. " +
                "Non puoi cambiare data, orari o squadra degli interventi futuri. Puoi aggiornare le note, " +
                "completare la visita oppure eliminare l'intervento; le correzioni dello storico rimangono consentite.");
    }

    internal static List<WorkScheduleEntry> RescheduleUpcomingPresets(IEnumerable<WorkScheduleEntry> entries,
        WorkScheduleSettings settings, DateTime today)
    {
        var changed = new List<WorkScheduleEntry>();
        foreach (var entry in entries)
        {
            if (entry.Date.Date < today.Date || entry.Status != WorkScheduleEntryStatus.Planned ||
                entry.SlotKind == WorkScheduleSlotKind.Custom || !entry.ReservesEmployees(today)) continue;
            var range = settings.GetRange(entry.SlotKind);
            if (entry.StartMinutes == range.Start && entry.EndMinutes == range.End) continue;
            var copy = entry.CreateValidatedCopy();
            copy.StartMinutes = range.Start;
            copy.EndMinutes = range.End;
            copy.SettingsRevision = settings.Revision;
            copy.Revision = checked(copy.Revision + 1);
            changed.Add(copy);
        }
        return changed;
    }

    // The end is excluded, so a morning ending at 12:00 can be followed by
    // another intervention starting at 12:00 for the same person.
    internal static void ValidateNoConflicts(IEnumerable<WorkScheduleEntry> entries, DateTime? today = null)
    {
        var currentDate = today?.Date ?? DateTime.Today;
        var assignments = entries.Where(entry => entry.ReservesEmployees(currentDate))
            .SelectMany(entry => entry.EmployeeIds.Select(employee => (employee, entry)))
            .GroupBy(x => (x.employee, x.entry.Date.Date));
        foreach (var group in assignments)
        {
            var ordered = group.Select(x => x.entry).OrderBy(x => x.StartMinutes).ThenBy(x => x.EndMinutes).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                var previous = ordered[i];
                for (int j = i + 1; j < ordered.Count && ordered[j].StartMinutes < previous.EndMinutes; j++)
                {
                    var next = ordered[j];
                    if (previous.Id == next.Id) continue;
                    var employee = previous.Employees.Concat(next.Employees).FirstOrDefault(x => x.Id == group.Key.employee);
                    string name = employee == null ? "Il dipendente selezionato" :
                        string.Join(" ", new[] { employee.FirstName, employee.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    throw new InvalidOperationException($"{name} ha due impegni sovrapposti il {previous.Date:dd/MM/yyyy}: " +
                        $"{Describe(previous)} ({previous.TimeDisplay}) e {Describe(next)} ({next.TimeDisplay}). " +
                        "Modifica gli orari o la squadra; nessuna modifica è stata salvata.");
                }
            }
        }
    }

    private static string Describe(WorkScheduleEntry entry) => entry.Kind == WorkScheduleEntryKind.Absence
        ? entry.AbsenceReason : string.IsNullOrWhiteSpace(entry.Title) ? $"ordine {entry.QuoteNumber}" : entry.Title;
}
