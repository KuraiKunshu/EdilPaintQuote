namespace EdilPaintPreventibiviGen.Models;

public sealed class InstallationCertificateContext
{
    public string QuoteNumber { get; set; } = string.Empty;
    public DateTime CompletionDate { get; set; } = DateTime.Today;
    public string WorkSite { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ReferenceName { get; set; } = string.Empty;
    public string SelectedLogo { get; set; } = string.Empty;
    public List<Item> Materials { get; set; } = [];
    public List<Customer> AllCustomers { get; set; } = [];
    public string PdfTemplateName { get; set; } = "Classico";
    public string FooterText { get; set; } = string.Empty;
}
