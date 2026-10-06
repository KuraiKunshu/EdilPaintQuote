namespace EdilPaintPreventibiviGen.Models;

public sealed class EmployeeSettingsModel
{
    public const int MaxNameLength = 250;
    public const int MaxAbbreviationLength = 24;
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Revision { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;

    public EmployeeSettingsModel CreateValidatedCopy()
    {
        if (string.IsNullOrWhiteSpace(FirstName))
            throw new InvalidOperationException("Inserisci il nome di ogni dipendente. Il cognome è facoltativo.");

        if (FirstName.Trim().Length > MaxNameLength || (LastName?.Trim().Length ?? 0) > MaxNameLength)
            throw new InvalidOperationException($"Nome e cognome possono contenere al massimo {MaxNameLength} caratteri ciascuno.");

        var abbreviation = Abbreviation?.Trim().ToUpperInvariant() ?? string.Empty;
        if (abbreviation.Length > MaxAbbreviationLength)
            throw new InvalidOperationException($"La sigla può contenere al massimo {MaxAbbreviationLength} caratteri.");

        return new EmployeeSettingsModel
        {
            Id = Id,
            Revision = Revision,
            FirstName = FirstName.Trim(),
            LastName = LastName?.Trim() ?? string.Empty,
            Abbreviation = abbreviation
        };
    }
}
