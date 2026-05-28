using System.IO;
using System.Security.Cryptography;
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

    public void DeleteQuoteFiles(QuoteHistoryEntry entry)
    {
        string expectedPath = GetExpectedPdfPath(entry);

        if (File.Exists(expectedPath))
        {
            File.Delete(expectedPath);

            string? parentDir = Path.GetDirectoryName(expectedPath);
            if (!string.IsNullOrWhiteSpace(parentDir))
            {
                string allegatiDir = Path.Combine(parentDir, "Allegati_" + entry.QuoteNumber);
                if (Directory.Exists(allegatiDir))
                    Directory.Delete(allegatiDir, true);
            }
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

            // Cerca ricorsivamente tutti i PDF che contengono il numero preventivo nel nome
            var matches = Directory.EnumerateFiles(customerFolder, "*.pdf", SearchOption.AllDirectories)
                .Where(f => Path.GetFileNameWithoutExtension(f)
                    .Contains(entry.QuoteNumber, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return matches.FirstOrDefault();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FindPdfByQuoteNumber] Errore: {ex.Message}");
            return null;
        }
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
