namespace EdilPaintPreventibiviGen.Services;

public sealed class BusinessSettingsModel
{
    // Missing settings preserve the existing EdilPaint installation.
    public bool EnableWindowAutomations { get; set; } = true;
    public bool EnableInstallationCertificate { get; set; } = true;
    public string StampFileName { get; set; } = string.Empty;
    public bool UseLegacyStamp { get; set; } = true;
}
