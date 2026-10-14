namespace EdilPaintPreventibiviGen.Data.Entities;

public sealed class PosTemplateEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public long Revision { get; set; }
    public bool IsArchived { get; set; }
    public string PayloadJson { get; set; } = "";
}

public sealed class PosDraftEntity
{
    public Guid Id { get; set; }
    public string QuoteNumber { get; set; } = "";
    public long Revision { get; set; }
    public DateTime SavedAtUtc { get; set; }
    public string PayloadJson { get; set; } = "";
}
