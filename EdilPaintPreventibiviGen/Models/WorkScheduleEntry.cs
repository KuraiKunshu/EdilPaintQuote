namespace EdilPaintPreventibiviGen.Models;

public sealed class WorkScheduleEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Revision { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public WorkScheduleEntryKind Kind { get; set; }
    public WorkScheduleSlotKind SlotKind { get; set; }
    public long SettingsRevision { get; set; }
    public int StartMinutes { get; set; } = 8 * 60;
    public int EndMinutes { get; set; } = 17 * 60;
    public string QuoteNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string ReferenceName { get; set; } = string.Empty;
    public string AbsenceReason { get; set; } = "Indisponibilità";
    public string Notes { get; set; } = string.Empty;
    public WorkScheduleEntryStatus Status { get; set; }
    public List<Guid> EmployeeIds { get; set; } = [];
    public List<EmployeeSettingsModel> Employees { get; set; } = [];
    public string MaterialStatus { get; set; } = string.Empty;
    public DateTime? ExpectedDeliveryDate { get; set; }
    public QuoteStatus? OrderStatus { get; set; }
    public bool IsOrderDeleted { get; set; }
    public bool? IsOrderActive { get; set; }
    public bool IsActiveOrder => !IsOrderDeleted &&
        (IsOrderActive ?? (!OrderStatus.HasValue || OrderStatus == QuoteStatus.Confermato));
    public bool ReservesEmployees(DateTime today) => Kind == WorkScheduleEntryKind.Absence ||
        Status == WorkScheduleEntryStatus.Completed || Date.Date < today.Date || IsActiveOrder;
    public string Title => Kind == WorkScheduleEntryKind.Absence ? AbsenceReason :
        WorkScheduleSiteDisplay.GetTitle(SiteName, ReferenceName, CustomerName, QuoteNumber);
    public string ReferenceDetail => Kind == WorkScheduleEntryKind.Absence ? string.Empty :
        WorkScheduleSiteDisplay.GetReferenceDetail(ReferenceName, Title);
    public string TimeDisplay => $"{WorkScheduleSettings.FormatTime(StartMinutes)}–{WorkScheduleSettings.FormatTime(EndMinutes)}";
    public string SlotDisplay => SlotKind switch
    {
        WorkScheduleSlotKind.FullDay => "Giornata intera", WorkScheduleSlotKind.Morning => "Mattina",
        WorkScheduleSlotKind.Afternoon => "Pomeriggio", _ => "Orario libero"
    };
    public bool HasMaterialWarning => Kind == WorkScheduleEntryKind.Job &&
        (MaterialStatus is "Da ordinare" or "Non disponibile" || ExpectedDeliveryDate?.Date > Date.Date);
    public bool HasOrderWarning => Kind == WorkScheduleEntryKind.Job && Status == WorkScheduleEntryStatus.Planned &&
        !IsActiveOrder;

    public WorkScheduleEntry CreateValidatedCopy()
    {
        if (Id == Guid.Empty || Date == default || !Enum.IsDefined(Kind) || !Enum.IsDefined(SlotKind) || !Enum.IsDefined(Status) ||
            EmployeeIds == null || Employees == null || Employees.Any(employee => employee == null) ||
            QuoteNumber == null || AbsenceReason == null)
            throw new InvalidOperationException("Dati dell'intervento non validi.");
        WorkScheduleSettings.ValidateRange(StartMinutes, EndMinutes);
        if (Kind == WorkScheduleEntryKind.Job && string.IsNullOrWhiteSpace(QuoteNumber))
            throw new InvalidOperationException("Seleziona un ordine confermato da programmare.");
        if (Kind == WorkScheduleEntryKind.Absence && (EmployeeIds.Count == 0 || string.IsNullOrWhiteSpace(AbsenceReason)))
            throw new InvalidOperationException("Per un'assenza indica il motivo e almeno un dipendente.");
        if (EmployeeIds.Any(x => x == Guid.Empty) || EmployeeIds.Distinct().Count() != EmployeeIds.Count)
            throw new InvalidOperationException("Selezione dipendenti non valida.");
        if (AbsenceReason.Length > 250 || QuoteNumber.Length > 50)
            throw new InvalidOperationException("Il testo inserito è troppo lungo.");
        var copy = (WorkScheduleEntry)MemberwiseClone();
        copy.Date = DateTime.SpecifyKind(Date.Date, DateTimeKind.Unspecified);
        copy.QuoteNumber = QuoteNumber.Trim();
        copy.AbsenceReason = AbsenceReason.Trim();
        copy.Notes = Notes?.Trim() ?? string.Empty;
        copy.EmployeeIds = [.. EmployeeIds];
        copy.Employees = Employees.Select(x => x.CreateValidatedCopy()).ToList();
        return copy;
    }
}

public sealed class WorkScheduleOrder
{
    public string QuoteNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string ReferenceName { get; set; } = string.Empty;
    public string MaterialStatus { get; set; } = string.Empty;
    public DateTime? ExpectedDeliveryDate { get; set; }
    public int PlannedInterventions { get; set; }
    public string Title => WorkScheduleSiteDisplay.GetTitle(SiteName, ReferenceName, CustomerName, QuoteNumber);
    public string ReferenceDetail => WorkScheduleSiteDisplay.GetReferenceDetail(ReferenceName, Title);
    public string CustomerDetail => string.IsNullOrWhiteSpace(CustomerName) ? "Cliente non indicato" : $"Cliente: {CustomerName.Trim()}";
    public string Display => string.Join(" · ", new[] { Title, ReferenceDetail, CustomerDetail, $"Ordine {QuoteNumber}" }
        .Where(part => !string.IsNullOrWhiteSpace(part)));
}

internal static class WorkScheduleSiteDisplay
{
    internal static string GetTitle(string? site, string? reference, string? customer, string? number)
    {
        if (!string.IsNullOrWhiteSpace(site)) return site.Trim();
        if (!string.IsNullOrWhiteSpace(reference)) return reference.Trim();
        if (!string.IsNullOrWhiteSpace(customer)) return customer.Trim();
        return string.IsNullOrWhiteSpace(number) ? "Cantiere non indicato" : $"Ordine {number.Trim()}";
    }

    internal static string GetReferenceDetail(string? reference, string title)
        => string.IsNullOrWhiteSpace(reference) || string.Equals(reference.Trim(), title, StringComparison.OrdinalIgnoreCase)
            ? string.Empty : $"Riferimento: {reference.Trim()}";
}
