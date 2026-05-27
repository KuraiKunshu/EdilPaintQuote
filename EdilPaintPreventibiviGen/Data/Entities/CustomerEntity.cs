namespace EdilPaintPreventibiviGen.Data.Entities;

public class CustomerEntity
{
	public int Id { get; set; }
	public string BusinessName { get; set; } = string.Empty;
	public string Address { get; set; } = string.Empty;
	public string Email { get; set; } = string.Empty;
	public string Phone { get; set; } = string.Empty;
	public double MaterialDiscount { get; set; }
	public double LaborDiscount { get; set; }
	public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;
	public ICollection<QuoteEntity> QuotesAsCustomer { get; set; } = new List<QuoteEntity>();
	public ICollection<QuoteEntity> QuotesAsReference { get; set; } = new List<QuoteEntity>();
}