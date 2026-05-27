namespace EdilPaintPreventibiviGen.Models;

public class CostsPdfContext
{
    public string QuoteNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string PartnerCompanyName { get; set; } = string.Empty;
    public List<CostAllocationItem> OurCosts { get; set; } = new();
    public List<CostAllocationItem> PartnerCosts { get; set; } = new();
    public List<CostAllocationItem> AdditionalCosts { get; set; } = new();
    public double Imponibile { get; set; }
    public double Total { get; set; }
}