using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed class QuoteHistoryService
{
    private readonly IDataService _dataService;
    private readonly StoragePathService _storagePathService;

    public QuoteHistoryService(IDataService dataService, StoragePathService storagePathService)
    {
        _dataService = dataService;
        _storagePathService = storagePathService;
    }

    public async Task<List<QuoteHistoryEntry>> LoadAsync()
    {
        return await _dataService.GetQuotesAsync();
    }
    
    public async Task<List<QuoteHistoryEntry>> LoadTopAsync(int count)
    {
        return await _dataService.GetQuotesAsync(Math.Max(1, count));
    }

    public async Task<List<QuoteHistorySummary>> LoadTopSummariesAsync(int count)
    {
        return await _dataService.GetQuoteSummariesAsync(Math.Max(1, count));
    }

    public async Task<List<QuoteHistorySummary>> SearchSummariesAsync(string text, int take)
    {
        return await _dataService.SearchQuoteSummariesAsync(text, take);
    }
    
    public async Task<QuoteHistoryEntry?> GetQuoteByNumberAsync(string quoteNumber)
    {
        return await _dataService.GetQuoteByNumberAsync(quoteNumber);
    }
    public async Task SaveSingleAsync(QuoteHistoryEntry entry)
    {
        await _dataService.SaveQuoteAsync(entry);
    }
    public Task UpdateNotesAsync(string quoteNumber, string notes) =>
        _dataService.UpdateQuoteNotesAsync(quoteNumber, notes);

    public Task UpdateStatusAsync(string quoteNumber, QuoteStatus status) =>
        _dataService.UpdateQuoteStatusAsync(quoteNumber, status);

    public async Task DeleteQuoteAsync(string quoteNumber)
    {
        await _dataService.DeleteQuoteAsync(quoteNumber);
    }

    public string GetExpectedPdfPath(QuoteHistoryEntry entry)
    {
        return _storagePathService.BuildQuotePdfPath(
            entry.CustomerName,
            entry.QuoteNumber,
            entry.Date,
            string.IsNullOrWhiteSpace(entry.ReferenceName) ? null : entry.ReferenceName);
    }

    public async Task<string> EnsureOfficialPdfExistsAsync(QuoteHistoryEntry entry)
    {
        string expectedPath = GetExpectedPdfPath(entry);
        byte[]? officialPdf = null;
        try
        {
            officialPdf = await _dataService.GetQuotePdfContentAsync(entry.QuoteNumber);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EnsureOfficialPdfExists] DB PDF non disponibile per {entry.QuoteNumber}: {ex.Message}");
        }

        if (officialPdf is not { Length: > 0 })
            officialPdf = entry.PdfFile?.Content is { Length: > 0 } ? entry.PdfFile.Content : null;

        if (officialPdf is not { Length: > 0 })
            return string.Empty;

        string? folder = Path.GetDirectoryName(expectedPath);
        if (!string.IsNullOrWhiteSpace(folder))
            Directory.CreateDirectory(folder);

        if (!File.Exists(expectedPath) || !FileMatchesBytes(expectedPath, officialPdf))
            await File.WriteAllBytesAsync(expectedPath, officialPdf);

        entry.PdfPath = expectedPath;
        return expectedPath;
    }

    public string GetExpectedCostsPdfPath(QuoteHistoryEntry entry)
    {
        return _storagePathService.BuildQuoteCostsPdfPath(
            entry.CustomerName,
            entry.QuoteNumber,
            entry.Date,
            string.IsNullOrWhiteSpace(entry.ReferenceName) ? null : entry.ReferenceName);
    }

    public async Task<string> EnsureCostsPdfExistsAsync(QuoteHistoryEntry entry)
    {
        byte[]? costsPdf = await _dataService.GetQuoteCostsPdfContentAsync(entry.QuoteNumber);
        if (costsPdf is not { Length: > 0 })
            return string.Empty;

        string expectedPath = GetExpectedCostsPdfPath(entry);
        string? folder = Path.GetDirectoryName(expectedPath);
        if (!string.IsNullOrWhiteSpace(folder))
            Directory.CreateDirectory(folder);

        if (!File.Exists(expectedPath) || !FileMatchesBytes(expectedPath, costsPdf))
            await File.WriteAllBytesAsync(expectedPath, costsPdf);

        return expectedPath;
    }

    public async Task EnsureAttachmentsFolderExistsAsync(QuoteHistoryEntry entry)
    {
        var attachments = await _dataService.GetQuoteAttachmentsAsync(entry.QuoteNumber);
        if (attachments.Count == 0)
            return;

        string? parentDir = Path.GetDirectoryName(GetExpectedPdfPath(entry));
        if (string.IsNullOrWhiteSpace(parentDir))
            return;

        string attachmentsDir = Path.Combine(parentDir, "Allegati_" + entry.QuoteNumber);
        Directory.CreateDirectory(attachmentsDir);

        foreach (var attachment in attachments.Where(x => x.Content.Length > 0))
        {
            string path = Path.Combine(attachmentsDir, Path.GetFileName(attachment.FileName));
            if (!File.Exists(path) || !FileMatchesBytes(path, attachment.Content))
                await File.WriteAllBytesAsync(path, attachment.Content);
        }
    }

    public void DeleteQuoteFiles(QuoteHistoryEntry entry)
    {
        string expectedPath = GetExpectedPdfPath(entry);
        string expectedCostsPath = GetExpectedCostsPdfPath(entry);

        if (File.Exists(expectedPath))
            File.Delete(expectedPath);

        if (File.Exists(expectedCostsPath))
            File.Delete(expectedCostsPath);

        string? parentDir = Path.GetDirectoryName(expectedPath);
        if (!string.IsNullOrWhiteSpace(parentDir))
        {
            if (Directory.Exists(parentDir))
            {
                foreach (string costsPath in Directory.EnumerateFiles(parentDir, "*_COSTI.pdf"))
                {
                    if (IsPdfForQuote(costsPath, entry.QuoteNumber, allowCostsPdf: true))
                        File.Delete(costsPath);
                }
            }

            string allegatiDir = Path.Combine(parentDir, "Allegati_" + entry.QuoteNumber);
            if (Directory.Exists(allegatiDir))
                Directory.Delete(allegatiDir, true);
        }
    }
    
    public string? FindPdfByQuoteNumber(QuoteHistoryEntry entry)
    {
        try
        {
            string customerFolder = _storagePathService.BuildCustomerPdfFolder(
                entry.CustomerName,
                string.IsNullOrWhiteSpace(entry.ReferenceName) ? null : entry.ReferenceName);

            if (!Directory.Exists(customerFolder))
            {
                // Prova anche senza reference (cartella livello superiore)
                customerFolder = _storagePathService.BuildCustomerPdfFolder(entry.CustomerName, null);
            }

            if (!Directory.Exists(customerFolder))
                return null;

            var matches = Directory.EnumerateFiles(customerFolder, "*.pdf", SearchOption.AllDirectories)
                .Where(f => IsPdfForQuote(f, entry.QuoteNumber, allowCostsPdf: false))
                .ToList();

            return matches.FirstOrDefault();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FindPdfByQuoteNumber] Errore: {ex.Message}");
            return null;
        }
    }

    public static bool IsPdfForQuote(string path, string quoteNumber, bool allowCostsPdf)
    {
        string fileName = Path.GetFileNameWithoutExtension(path);
        if (!allowCostsPdf && fileName.EndsWith("_COSTI", StringComparison.OrdinalIgnoreCase))
            return false;

        string pattern = $@"(?<![A-Za-z0-9-]){Regex.Escape(quoteNumber)}(?![A-Za-z0-9-])";
        return Regex.IsMatch(fileName, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool FileMatchesBytes(string path, byte[] content)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != content.Length)
                return false;

            using var fileStream = File.OpenRead(path);
            using var sha = SHA256.Create();
            byte[] fileHash = sha.ComputeHash(fileStream);
            byte[] contentHash = SHA256.HashData(content);
            return fileHash.SequenceEqual(contentHash);
        }
        catch
        {
            return false;
        }
    }
}
