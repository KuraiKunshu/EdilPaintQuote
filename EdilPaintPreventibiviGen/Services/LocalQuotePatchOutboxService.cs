using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed class LocalQuotePatchOutboxService
{
    private readonly string _outboxPath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public LocalQuotePatchOutboxService(string dataPath)
    {
        _outboxPath = Path.Combine(dataPath, "PendingQuotePatches");
        Directory.CreateDirectory(_outboxPath);
    }

    public Task StoreNotesAsync(string quoteNumber, string notes, CancellationToken cancellationToken = default) =>
        UpdateAsync(quoteNumber, patch => patch.Notes = notes, cancellationToken);

    public Task StoreStatusAsync(string quoteNumber, QuoteStatus status, CancellationToken cancellationToken = default) =>
        UpdateAsync(quoteNumber, patch => patch.Status = status, cancellationToken);

    public async Task<List<PendingQuotePatch>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        var patches = new List<PendingQuotePatch>();
        foreach (string path in Directory.EnumerateFiles(_outboxPath, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string json = await File.ReadAllTextAsync(path, cancellationToken);
                var patch = JsonSerializer.Deserialize<PendingQuotePatch>(json, JsonOptions);
                if (!string.IsNullOrWhiteSpace(patch?.QuoteNumber))
                    patches.Add(patch);
            }
            catch (JsonException)
            {
            }
        }

        return patches;
    }

    public Task RemoveAsync(string quoteNumber)
    {
        string path = BuildPath(quoteNumber);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    private async Task UpdateAsync(
        string quoteNumber,
        Action<PendingQuotePatch> update,
        CancellationToken cancellationToken)
    {
        string path = BuildPath(quoteNumber);
        PendingQuotePatch patch;
        if (File.Exists(path))
        {
            string existing = await File.ReadAllTextAsync(path, cancellationToken);
            patch = JsonSerializer.Deserialize<PendingQuotePatch>(existing, JsonOptions) ?? new PendingQuotePatch();
        }
        else
        {
            patch = new PendingQuotePatch();
        }

        patch.QuoteNumber = quoteNumber;
        patch.UpdatedAtUtc = DateTime.UtcNow;
        update(patch);
        await LocalDeletionOutboxService.WriteAtomicAsync(path, patch, cancellationToken);
    }

    private string BuildPath(string quoteNumber)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(quoteNumber));
        return Path.Combine(_outboxPath, Convert.ToHexString(hash) + ".json");
    }
}

public sealed class PendingQuotePatch
{
    public string QuoteNumber { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public QuoteStatus? Status { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
