namespace EdilPaintPreventibiviGen.Data.Entities;

public sealed class EmployeeEntity
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public long Revision { get; set; } = 1;
    public bool IsDeleted { get; set; }
}
