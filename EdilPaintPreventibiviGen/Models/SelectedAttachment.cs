namespace EdilPaintPreventibiviGen.Models;

public class SelectedAttachment
{
	public string FileName { get; set; } = string.Empty;
	public string FilePath { get; set; } = string.Empty;
	public string ContentType { get; set; } = "application/octet-stream";
	public byte[] Content { get; set; } = [];
}