using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed class LocalQuotePatchOutboxService
{
    private readonly string _outboxPath;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
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

    public Task StoreSendInfoAsync(string quoteNumber, QuoteSendInfo sendInfo, CancellationToken cancellationToken = default) =>
        UpdateAsync(quoteNumber, patch => patch.SendInfo = sendInfo, cancellationToken);

    public Task StoreReminderAsync(string quoteNumber, QuoteReminderInfo reminderInfo, CancellationToken cancellationToken = default) =>
        UpdateAsync(quoteNumber, patch => patch.ReminderInfo = reminderInfo, cancellationToken);

    public Task StoreSupplierInfoAsync(string quoteNumber, QuoteSupplierInfo supplierInfo, CancellationToken cancellationToken = default) =>
        UpdateAsync(quoteNumber, patch => patch.SupplierInfo = supplierInfo, cancellationToken);

    public async Task<List<PendingQuotePatch>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
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
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task RemoveAsync(
        string quoteNumber,
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            string path = BuildPath(quoteNumber);
            if (File.Exists(path))
                File.Delete(path);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public Task RemoveNotesIfMatchesAsync(
        string quoteNumber,
        string appliedNotes,
        CancellationToken cancellationToken = default) =>
        RemoveAppliedAsync(
            quoteNumber,
            patch =>
            {
                if (!string.Equals(patch.Notes, appliedNotes, StringComparison.Ordinal))
                    return false;

                patch.Notes = null;
                return true;
            },
            cancellationToken);

    public Task RemoveStatusIfMatchesAsync(
        string quoteNumber,
        QuoteStatus appliedStatus,
        CancellationToken cancellationToken = default) =>
        RemoveAppliedAsync(
            quoteNumber,
            patch =>
            {
                if (patch.Status != appliedStatus)
                    return false;

                patch.Status = null;
                return true;
            },
            cancellationToken);

    public Task RemoveSendInfoIfMatchesAsync(
        string quoteNumber,
        QuoteSendInfo appliedSendInfo,
        CancellationToken cancellationToken = default) =>
        RemoveAppliedAsync(
            quoteNumber,
            patch =>
            {
                if (!SendInfoEquals(patch.SendInfo, appliedSendInfo))
                    return false;

                patch.SendInfo = null;
                return true;
            },
            cancellationToken);

    public Task RemoveReminderInfoIfMatchesAsync(
        string quoteNumber,
        QuoteReminderInfo appliedReminderInfo,
        CancellationToken cancellationToken = default) =>
        RemoveAppliedAsync(
            quoteNumber,
            patch =>
            {
                if (!ReminderInfoEquals(patch.ReminderInfo, appliedReminderInfo))
                    return false;

                patch.ReminderInfo = null;
                return true;
            },
            cancellationToken);

    public Task RemoveSupplierInfoIfMatchesAsync(
        string quoteNumber,
        QuoteSupplierInfo appliedSupplierInfo,
        CancellationToken cancellationToken = default) =>
        RemoveAppliedAsync(
            quoteNumber,
            patch =>
            {
                if (!SupplierInfoEquals(patch.SupplierInfo, appliedSupplierInfo))
                    return false;

                patch.SupplierInfo = null;
                return true;
            },
            cancellationToken);

    private async Task RemoveAppliedAsync(
        string quoteNumber,
        Func<PendingQuotePatch, bool> tryClearApplied,
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            string path = BuildPath(quoteNumber);
            if (!File.Exists(path))
                return;

            string existing = await File.ReadAllTextAsync(path, cancellationToken);
            var patch = JsonSerializer.Deserialize<PendingQuotePatch>(existing, JsonOptions);
            if (patch == null)
            {
                File.Delete(path);
                return;
            }

            if (!tryClearApplied(patch))
                return;

            if (patch.IsEmpty)
            {
                File.Delete(path);
                return;
            }

            await LocalDeletionOutboxService.WriteAtomicAsync(path, patch, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task UpdateAsync(
        string quoteNumber,
        Action<PendingQuotePatch> update,
        CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
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
        finally
        {
            _semaphore.Release();
        }
    }

    private static bool SendInfoEquals(QuoteSendInfo? current, QuoteSendInfo applied) =>
        current != null &&
        current.SentAtUtc == applied.SentAtUtc &&
        string.Equals(current.Method, applied.Method, StringComparison.Ordinal) &&
        string.Equals(current.Recipient, applied.Recipient, StringComparison.Ordinal) &&
        string.Equals(current.DeviceName, applied.DeviceName, StringComparison.Ordinal);

    private static bool ReminderInfoEquals(QuoteReminderInfo? current, QuoteReminderInfo applied) =>
        current != null &&
        current.ReminderAtUtc == applied.ReminderAtUtc &&
        string.Equals(current.DeviceName, applied.DeviceName, StringComparison.Ordinal);

    private static bool SupplierInfoEquals(QuoteSupplierInfo? current, QuoteSupplierInfo applied) =>
        current != null &&
        string.Equals(current.SupplierName, applied.SupplierName, StringComparison.Ordinal) &&
        current.MaterialsOrderedByCustomer == applied.MaterialsOrderedByCustomer &&
        current.MaterialOrderDate == applied.MaterialOrderDate &&
        current.ExpectedDeliveryDate == applied.ExpectedDeliveryDate &&
        string.Equals(current.MaterialStatus, applied.MaterialStatus, StringComparison.Ordinal) &&
        string.Equals(current.DeviceName, applied.DeviceName, StringComparison.Ordinal);

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
    public QuoteSendInfo? SendInfo { get; set; }
    public QuoteReminderInfo? ReminderInfo { get; set; }
    public QuoteSupplierInfo? SupplierInfo { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public bool IsEmpty =>
        Notes == null &&
        !Status.HasValue &&
        SendInfo == null &&
        ReminderInfo == null &&
        SupplierInfo == null;
}
