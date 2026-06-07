namespace EdilPaintPreventibiviGen.Models;

public class PdfGenerationContext
{
    public string QuoteNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string PaymentTerms { get; set; } = string.Empty;
    public string IvaType { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ReferenceName { get; set; } = string.Empty;
    public string SelectedLogo { get; set; } = string.Empty;
    public double MaterialDiscount { get; set; }
    public double LaborDiscount { get; set; }
    public List<Item> Materials { get; set; } = new();
    public List<Item> Labors { get; set; } = new();
    public double Imponibile { get; set; }
    public double Total { get; set; }
    public List<StoredFile> Attachments { get; set; } = new();
    public List<Customer> AllCustomers { get; set; } = new();
    public string PdfTemplateName { get; set; } = "Standard";
    public string PdfNotesTitle { get; set; } = "NOTE E TERMINI DI PAGAMENTO";
    public string PdfFooterText { get; set; } = string.Empty;
    public string PdfSignatureText { get; set; } = "Firma per accettazione";
    public bool PdfShowTemplateName { get; set; }
}
