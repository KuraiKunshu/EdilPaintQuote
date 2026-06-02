using System.IO;
using System.Text.Json;

namespace EdilPaintPreventibiviGen.Services;

public sealed class LocalDeletionOutboxService
{
    private readonly string _path;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public LocalDeletionOutboxService(string dataPath)
    {
        _path = Path.Combine(dataPath, "PendingDeletes.json");
    }

    public Task AddQuoteAsync(string quoteNumber, CancellationToken cancellationToken = default) =>
        UpdateAsync(state =>
        {
            state.Quotes.RemoveAll(x => x.QuoteNumber.Equals(quoteNumber, StringComparison.OrdinalIgnoreCase));
            state.Quotes.Add(new PendingQuoteDeletion { QuoteNumber = quoteNumber, DeletedAtUtc = DateTime.UtcNow });
        }, cancellationToken);

    public Task AddCustomerAsync(Guid syncId, string businessName, CancellationToken cancellationToken = default) =>
        UpdateAsync(state =>
        {
            state.Customers.RemoveAll(x =>
                (syncId != Guid.Empty && x.SyncId == syncId) ||
                x.BusinessName.Equals(businessName, StringComparison.OrdinalIgnoreCase));
            state.Customers.Add(new PendingCustomerDeletion
            {
                SyncId = syncId,
                BusinessName = businessName,
                DeletedAtUtc = DateTime.UtcNow
            });
        }, cancellationToken);

    public Task RemoveQuoteAsync(string quoteNumber, CancellationToken cancellationToken = default) =>
        UpdateAsync(state =>
            state.Quotes.RemoveAll(x => x.QuoteNumber.Equals(quoteNumber, StringComparison.OrdinalIgnoreCase)),
            cancellationToken);

    public Task RemoveCustomerAsync(Guid syncId, string businessName, CancellationToken cancellationToken = default) =>
        UpdateAsync(state =>
            state.Customers.RemoveAll(x =>
                (syncId != Guid.Empty && x.SyncId == syncId) ||
                x.BusinessName.Equals(businessName, StringComparison.OrdinalIgnoreCase)),
            cancellationToken);

    public async Task<PendingDeletes> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return await LoadInternalAsync(cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task UpdateAsync(Action<PendingDeletes> update, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadInternalAsync(cancellationToken);
            update(state);
            await WriteAtomicAsync(_path, state, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<PendingDeletes> LoadInternalAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return new PendingDeletes();

        try
        {
            string json = await File.ReadAllTextAsync(_path, cancellationToken);
            return JsonSerializer.Deserialize<PendingDeletes>(json, JsonOptions) ?? new PendingDeletes();
        }
        catch (JsonException)
        {
            return new PendingDeletes();
        }
    }

    internal static async Task WriteAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string temporaryPath = path + ".tmp";
        string json = JsonSerializer.Serialize(value, JsonOptions);
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);

        if (File.Exists(path))
            File.Copy(path, path + ".backup", overwrite: true);

        File.Move(temporaryPath, path, overwrite: true);
    }
}

public sealed class PendingDeletes
{
    public List<PendingQuoteDeletion> Quotes { get; set; } = [];
    public List<PendingCustomerDeletion> Customers { get; set; } = [];
}

public sealed class PendingQuoteDeletion
{
    public string QuoteNumber { get; set; } = string.Empty;
    public DateTime DeletedAtUtc { get; set; }
}

public sealed class PendingCustomerDeletion
{
    public Guid SyncId { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public DateTime DeletedAtUtc { get; set; }
}
