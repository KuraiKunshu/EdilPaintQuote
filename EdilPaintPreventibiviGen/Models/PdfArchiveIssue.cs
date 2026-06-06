namespace EdilPaintPreventibiviGen.Models;

public enum PdfArchiveIssueType
{
    OfficialPdfMissing,
    OfficialPdfMissingInDatabase,
    CostsPdfMissing,
    CostsPdfMissingInDatabase,
    AttachmentsMissing
}

public sealed class PdfArchiveIssue
{
    public string QuoteNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime QuoteDate { get; set; }
    public PdfArchiveIssueType Type { get; set; }
    public string Details { get; set; } = string.Empty;
    public string ExpectedPath { get; set; } = string.Empty;
    public bool CanRestore { get; set; }

    public string TypeDisplay => Type switch
    {
        PdfArchiveIssueType.OfficialPdfMissing => "PDF preventivo mancante",
        PdfArchiveIssueType.OfficialPdfMissingInDatabase => "PDF non presente nel database",
        PdfArchiveIssueType.CostsPdfMissing => "PDF costi mancante",
        PdfArchiveIssueType.CostsPdfMissingInDatabase => "PDF costi non presente nel database",
        PdfArchiveIssueType.AttachmentsMissing => "Allegati mancanti",
        _ => "Problema PDF"
    };

    public string RestoreDisplay => CanRestore ? "Ripristinabile" : "Da rigenerare";
}
