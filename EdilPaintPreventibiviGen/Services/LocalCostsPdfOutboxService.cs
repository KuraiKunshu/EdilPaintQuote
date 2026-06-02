using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed class LocalCostsPdfOutboxService
{
    private readonly string _outboxPath;

    public LocalCostsPdfOutboxService(string dataPath)
    {
        _outboxPath = Path.Combine(dataPath, "PendingCostsPdfs");
        Directory.CreateDirectory(_outboxPath);
    }

    public async Task StoreAsync(
        string quoteNumber,
        StoredFile file,
        CancellationToken cancellationToken = default)
    {
        if (file.Content.Length == 0)
            return;

        var snapshot = new CostsPdfSnapshot
        {
            QuoteNumber = quoteNumber,
            FileName = file.FileName,
            ContentType = file.ContentType,
            Content = file.Content,
            ImportedAt = file.ImportedAt
        };

        string path = BuildPath(quoteNumber);
        string temporaryPath = path + ".tmp";
        string json = JsonSerializer.Serialize(snapshot);
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    public async Task<StoredFile?> TryReadAsync(
        string quoteNumber,
        CancellationToken cancellationToken = default)
    {
        string path = BuildPath(quoteNumber);
        if (!File.Exists(path))
            return null;

        string json = await File.ReadAllTextAsync(path, cancellationToken);
        var snapshot = JsonSerializer.Deserialize<CostsPdfSnapshot>(json);
        if (snapshot == null)
            return null;

        return new StoredFile
        {
            FileName = snapshot.FileName,
            ContentType = snapshot.ContentType,
            Content = snapshot.Content,
            ImportedAt = snapshot.ImportedAt
        };
    }

    public async Task<List<string>> GetPendingQuoteNumbersAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<string>();
        foreach (string path in Directory.EnumerateFiles(_outboxPath, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string json = await File.ReadAllTextAsync(path, cancellationToken);
                var snapshot = JsonSerializer.Deserialize<CostsPdfSnapshot>(json);
                if (!string.IsNullOrWhiteSpace(snapshot?.QuoteNumber))
                    result.Add(snapshot.QuoteNumber);
            }
            catch (JsonException)
            {
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Task RemoveAsync(string quoteNumber)
    {
        string path = BuildPath(quoteNumber);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    private string BuildPath(string quoteNumber)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(quoteNumber));
        return Path.Combine(_outboxPath, Convert.ToHexString(hash) + ".json");
    }

    private sealed class CostsPdfSnapshot
    {
        public string QuoteNumber { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/pdf";
        public byte[] Content { get; set; } = [];
        public DateTime ImportedAt { get; set; }
    }
}
