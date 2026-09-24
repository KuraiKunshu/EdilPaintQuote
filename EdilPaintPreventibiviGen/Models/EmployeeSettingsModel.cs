namespace EdilPaintPreventibiviGen.Models;

public sealed class EmployeeSettingsModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    public EmployeeSettingsModel CreateValidatedCopy()
    {
        if (string.IsNullOrWhiteSpace(FirstName))
            throw new InvalidOperationException("Inserisci il nome di ogni dipendente. Il cognome è facoltativo.");

        return new EmployeeSettingsModel
        {
            Id = Id,
            FirstName = FirstName.Trim(),
            LastName = LastName?.Trim() ?? string.Empty
        };
    }
}
