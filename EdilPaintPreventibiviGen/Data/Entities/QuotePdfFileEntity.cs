namespace EdilPaintPreventibiviGen.Data.Entities;

public class QuotePdfFileEntity
{
	public int Id { get; set; }
	public int QuoteId { get; set; }
	public QuoteEntity Quote { get; set; } = null!;

	public string FileName { get; set; } = string.Empty;
	public string ContentType { get; set; } = "application/pdf";
	public byte[] Content { get; set; } = [];
	public DateTime ImportedAt { get; set; }
}