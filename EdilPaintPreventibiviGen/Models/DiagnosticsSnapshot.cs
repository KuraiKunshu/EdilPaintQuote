namespace EdilPaintPreventibiviGen.Models;

public sealed class DiagnosticsSnapshot
{
    public string AppVersion { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string SettingsPath { get; set; } = string.Empty;
    public string SettingsDirectory { get; set; } = string.Empty;
    public string PdfRootPath { get; set; } = string.Empty;
    public string LocalDataPath { get; set; } = string.Empty;
    public string DatabaseStatus { get; set; } = string.Empty;
    public string SyncStatus { get; set; } = string.Empty;
    public string LastSync { get; set; } = string.Empty;
    public string UpdaterStatus { get; set; } = string.Empty;
    public string UpdaterStatePath { get; set; } = string.Empty;
    public string PdfTemplateName { get; set; } = string.Empty;
    public int PendingPdfs { get; set; }
    public int PendingAttachments { get; set; }
    public int PendingCostsPdfs { get; set; }
    public int PendingQuotePatches { get; set; }
    public int PendingQuoteDeletes { get; set; }
    public int PendingCustomerDeletes { get; set; }
}
