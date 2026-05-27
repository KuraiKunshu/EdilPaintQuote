namespace EdilPaintPreventibiviGen.Data.Entities;

public class CompanySettingsEntity
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Indirizzo { get; set; } = string.Empty;
    public string Piva { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string SelectedLogo { get; set; } = string.Empty;
    public string LogosJson { get; set; } = string.Empty;
    public int LogoIndex { get; set; }
    public int Counter { get; set; }
    public string PaymentTerms { get; set; } = string.Empty;
}