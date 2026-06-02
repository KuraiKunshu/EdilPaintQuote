using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed class LocalAttachmentOutboxService
{
    private readonly string _outboxPath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public LocalAttachmentOutboxService(string dataPath)
    {
        _outboxPath = Path.Combine(dataPath, "PendingAttachments");
        Directory.CreateDirectory(_outboxPath);
    }

    public async Task StoreAsync(
        string quoteNumber,
        IEnumerable<StoredFile> attachments,
        CancellationToken cancellationToken = default)
    {
        var snapshot = new AttachmentSnapshot
        {
            QuoteNumber = quoteNumber,
            Attachments = attachments.Select(file => new AttachmentSnapshotItem
            {
                FileName = file.FileName,
                ContentType = file.ContentType,
                Content = file.Content,
                ImportedAt = file.ImportedAt
            }).ToList()
        };

        string path = BuildPath(quoteNumber);
        string temporaryPath = path + ".tmp";
        string json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    public async Task<List<StoredFile>?> TryReadAsync(
        string quoteNumber,
        CancellationToken cancellationToken = default)
    {
        string path = BuildPath(quoteNumber);
        if (!File.Exists(path))
            return null;

        string json = await File.ReadAllTextAsync(path, cancellationToken);
        var snapshot = JsonSerializer.Deserialize<AttachmentSnapshot>(json, JsonOptions);
        return snapshot?.Attachments.Select(file => new StoredFile
        {
            FileName = file.FileName,
            ContentType = file.ContentType,
            Content = file.Content,
            ImportedAt = file.ImportedAt
        }).ToList() ?? [];
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
                var snapshot = JsonSerializer.Deserialize<AttachmentSnapshot>(json, JsonOptions);
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

    private sealed class AttachmentSnapshot
    {
        public string QuoteNumber { get; set; } = string.Empty;
        public List<AttachmentSnapshotItem> Attachments { get; set; } = [];
    }

    private sealed class AttachmentSnapshotItem
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/octet-stream";
        public byte[] Content { get; set; } = [];
        public DateTime ImportedAt { get; set; }
    }
}
