namespace EdilPaintPreventibiviGen.Data.Entities;

public class QuoteLaborEntity
{
	public int Id { get; set; }
	public int QuoteId { get; set; }
	public QuoteEntity Quote { get; set; } = null!;

	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public double UnitPrice { get; set; }
	public int Quantity { get; set; }
	public double Discount { get; set; }
	public bool IsSignificant { get; set; }
	public int SortOrder { get; set; }
}
