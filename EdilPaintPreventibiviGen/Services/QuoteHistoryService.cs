using System.IO;
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

    public async Task SaveAsync(IEnumerable<QuoteHistoryEntry> history)
    {
        foreach (var entry in history)
            await _dataService.SaveQuoteAsync(entry);
    }

    public string GetExpectedPdfPath(QuoteHistoryEntry entry)
    {
        return _storagePathService.BuildQuotePdfPath(
            entry.CustomerName,
            entry.QuoteNumber,
            entry.Date,
            string.IsNullOrWhiteSpace(entry.ReferenceName) ? null : entry.ReferenceName);
    }

    public string EnsurePdfExists(QuoteHistoryEntry entry)
    {
        byte[]? pdfBytes = entry.PdfFile?.Content;

        string expectedPath = GetExpectedPdfPath(entry);
        string ensuredPath = _storagePathService.EnsurePdfExists(
            entry.CustomerName,
            entry.QuoteNumber,
            entry.Date,
            pdfBytes,
            string.IsNullOrWhiteSpace(entry.ReferenceName) ? null : entry.ReferenceName,
            currentPath: null);

        if (!string.IsNullOrWhiteSpace(ensuredPath))
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
}