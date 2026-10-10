namespace EdilPaintPreventibiviGen.Data.Entities;

public sealed class AutomaticMaterialSettingsEntity
{
    public int Id { get; set; }
    public long Revision { get; set; }
    public bool IsConfigured { get; set; }
    public string SettingsJson { get; set; } = "";
}
