using System.Text.Json;

namespace EdilPaintPreventibiviGen.Android.Services;

public sealed class MobileSettings
{
    public string SmtpServer { get; set; } = "smtp.libero.it";
    public int Port { get; set; } = 465;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string SenderEmail { get; set; } = "";
    public string SenderName { get; set; } = "EdilPaint";
    public string QuoteSubject { get; set; } = "Preventivo {QuoteNumber}";
    public string QuoteBody { get; set; } = "Buongiorno,\n\nin allegato il preventivo n. {QuoteNumber}.\n\nCordiali saluti";
    public string OrderSubject { get; set; } = "Ordine Riferimento {OrderReference}";
    public string OrderBody { get; set; } = "{Materials}";
    public int Workers { get; set; } = 2;
    public double Days { get; set; } = 1;
    public double HoursPerDay { get; set; } = 10;
    public double HourlyCost { get; set; } = 40;
    public double ProfitReductionPercentage { get; set; }
    public List<string> WindowPrefixes { get; set; } = ["GGL", "GGU", "GPL", "GPU", "Q4", "R8"];
    public List<EdilPaintPreventibiviGen.Models.AutomaticWindowMaterialRule> MaterialRules { get; set; } =
    [new() { RuleId = "internal-finish", LaborNameSnapshot = "Finitura interna", MaterialNameSnapshot = "Perline" }];

    public static async Task<MobileSettings> LoadAsync()
    {
        string? value = await SecureStorage.Default.GetAsync("mobile_settings_v1");
        return string.IsNullOrEmpty(value) ? new() : JsonSerializer.Deserialize<MobileSettings>(value) ?? new();
    }

    public Task SaveAsync() => SecureStorage.Default.SetAsync("mobile_settings_v1", JsonSerializer.Serialize(this));
}
