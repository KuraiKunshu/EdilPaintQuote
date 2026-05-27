using System.Text.Json.Serialization;
namespace EdilPaintPreventibiviGen.Models;

public class StoredFile
{
	public string FileName { get; set; } = string.Empty;
	public string ContentType { get; set; } = "application/octet-stream";
	[JsonIgnore]
	public byte[] Content { get; set; } = [];
	public DateTime ImportedAt { get; set; } = DateTime.Now;
}