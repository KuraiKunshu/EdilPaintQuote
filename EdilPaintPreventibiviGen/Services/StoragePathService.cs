using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace EdilPaintPreventibiviGen.Services;

public class StoragePathService
{
    #region Fields
    private static StoragePathService? _instance;
    public static StoragePathService Instance => _instance
        ?? throw new InvalidOperationException("StoragePathService non è stato ancora inizializzato. Chiama Initialize() prima.");

    private readonly PdfStorageSettingsModel _pdfStorage;
    #endregion

    #region Constructor
    private StoragePathService(AppSettingsService appSettings)
    {
        _pdfStorage = appSettings.PdfStorage;
    }

    
    /// <summary>
    /// Inizializza il singleton con le impostazioni già caricate da App.xaml.cs.
    /// Deve essere chiamato UNA SOLA VOLTA all'avvio.
    /// </summary>
    public static void Initialize(AppSettingsService appSettings)
    {
        _instance = new StoragePathService(appSettings);
    }
    #endregion

    #region Public Path Methods
    public string GetPdfRootPath()
    {
        if (string.IsNullOrWhiteSpace(_pdfStorage.RootPath))
            throw new InvalidOperationException("PdfStorage:RootPath non configurato in appsettings.json.");

        return _pdfStorage.RootPath;
    }

    public string GetHistoryFolder()
    {
        string root = GetPdfRootPath();
        string historySubFolder = string.IsNullOrWhiteSpace(_pdfStorage.HistorySubFolder)
            ? "Storico"
            : _pdfStorage.HistorySubFolder;

        return Path.Combine(root, SanitizeFolderName(historySubFolder));
    }

    public string BuildCustomerPdfFolder(string customerName, string? referenceName = null)
    {
        string root = GetPdfRootPath();

        string customerFolder = ApplyFolderPattern(
            _pdfStorage.CustomerFolderPattern,
            customerName,
            referenceName);

        if (string.IsNullOrWhiteSpace(customerFolder))
            customerFolder = SanitizeFolderName(customerName);

        string folder = Path.Combine(root, customerFolder);

        if (!string.IsNullOrWhiteSpace(referenceName))
            folder = Path.Combine(folder, SanitizeFolderName(referenceName));

        return folder;
    }

    public string BuildQuotePdfPath(string customerName, string quoteNumber, DateTime date, string? referenceName = null)
    {
        string folder = BuildCustomerPdfFolder(customerName, referenceName);

        string fileName = ApplyFilePattern(
            _pdfStorage.PdfFileNamePattern,
            customerName,
            referenceName,
            quoteNumber,
            date);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            // Fallback: usa customer (o reference se presente) + numero preventivo
            string namePrefix = !string.IsNullOrWhiteSpace(referenceName)
                ? SanitizeFolderName(referenceName)
                : SanitizeFolderName(customerName);
            fileName = $"{namePrefix}_Preventivo_{quoteNumber}.pdf";
        }

        return Path.Combine(folder, fileName);
    }

    public void EnsureFolderExists(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);
    }

    public void OpenFolder(string folderPath)
    {
        EnsureFolderExists(folderPath);

        Process.Start(new ProcessStartInfo
        {
            FileName = folderPath,
            UseShellExecute = true
        });
    }

    #endregion

    #region Helpers
    private static string ApplyFolderPattern(string? pattern, string customerName, string? referenceName)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return string.Empty;

        string result = pattern
            .Replace("{CustomerName}", SanitizeFolderName(customerName))
            .Replace("{ReferenceName}", string.IsNullOrWhiteSpace(referenceName) ? string.Empty : SanitizeFolderName(referenceName))
            .Replace("/", "_")
            .Replace("\\", "_")
            .Trim();

        return string.IsNullOrWhiteSpace(result) ? string.Empty : result;
    }

    private static string ApplyFilePattern(string? pattern, string customerName, string? referenceName, string quoteNumber, DateTime date)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return string.Empty;

        string result = pattern
            .Replace("{CustomerName}", SanitizeFolderName(customerName))
            .Replace("{ReferenceName}", string.IsNullOrWhiteSpace(referenceName) ? string.Empty : SanitizeFolderName(referenceName))
            .Replace("{QuoteNumber}", quoteNumber ?? string.Empty)
            .Replace("{Date}", date.ToString("dd-MM-yyyy"))
            .Replace("/", "_")
            .Replace("\\", "_")
            .Trim();

        return string.IsNullOrWhiteSpace(result) ? string.Empty : result;
    }

    public static string SanitizeFolderName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "N_A";

        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(value
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray());

        cleaned = cleaned
            .Replace("/", "_")
            .Replace("\\", "_")
            .TrimEnd('.')      // ← NUOVO: Windows non accetta cartelle che finiscono con punto
            .TrimEnd(' ')      // ← NUOVO: né con spazio finale
            .Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? "N_A" : cleaned;
    }
    #endregion
}
