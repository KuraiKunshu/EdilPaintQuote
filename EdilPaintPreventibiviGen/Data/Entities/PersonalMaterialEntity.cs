namespace EdilPaintPreventibiviGen.Data.Entities;

public class PersonalMaterialEntity
{
	public int Id { get; set; }
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public double UnitPrice { get; set; }
	public bool IsSignificant { get; set; }
}