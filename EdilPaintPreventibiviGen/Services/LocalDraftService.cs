using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed class LocalDraftService
{
    private const int EnvelopeVersion = 1;
    private const int BufferSize = 16 * 1024;
    private const int HashChunkSize = 64 * 1024;
    private const string ContentFileExtension = ".bin";

    private readonly string _draftPath;
    private readonly string _contentDirectory;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly ConditionalWeakTable<byte[], CachedContentKey> _contentKeyCache = new();
    private string? _lastSavedFingerprint;
    private bool _requiresEnvelopeMigration;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public LocalDraftService(string dataPath)
    {
        string draftDirectory = Path.Combine(dataPath, "Drafts");
        Directory.CreateDirectory(draftDirectory);
        _draftPath = Path.Combine(draftDirectory, "current-draft.json");
        _contentDirectory = Path.Combine(draftDirectory, "Content");
    }

    public async Task<QuoteHistoryEntry?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_draftPath))
            {
                ResetCachedState();
                return null;
            }

            await using var stream = OpenReadStream(_draftPath);
            DraftEnvelope? envelope;
            try
            {
                envelope = await JsonSerializer.DeserializeAsync<DraftEnvelope>(
                        stream,
                        JsonOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException)
            {
                envelope = null;
            }

            QuoteHistoryEntry? draft;
            if (envelope is { Version: EnvelopeVersion, Draft: not null })
            {
                draft = envelope.Draft;
                await HydrateEnvelopeContentAsync(envelope, cancellationToken).ConfigureAwait(false);
                _requiresEnvelopeMigration = false;
            }
            else
            {
                if (envelope is { Version: > 0 })
                    throw new InvalidDataException($"Versione bozza non supportata: {envelope.Version}.");

                stream.Position = 0;
                draft = await DeserializeLegacyDraftAsync(stream, cancellationToken).ConfigureAwait(false);
                _requiresEnvelopeMigration = draft != null;
            }

            if (draft == null)
            {
                ResetCachedState();
                return null;
            }

            var prepared = await PrepareSaveAsync(draft, cancellationToken).ConfigureAwait(false);
            _lastSavedFingerprint = prepared.Fingerprint;
            return draft;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ResetCachedState();
            Debug.WriteLine($"[Draft] Impossibile leggere '{_draftPath}': {ex}");
            return null;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task SaveAsync(QuoteHistoryEntry draft, CancellationToken cancellationToken = default) =>
        _ = await SaveIfChangedAsync(draft, cancellationToken).ConfigureAwait(false);

    public async Task<bool> SaveIfChangedAsync(
        QuoteHistoryEntry draft,
        CancellationToken cancellationToken = default,
        bool forceWrite = false)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            draft.Status = QuoteStatus.Bozza;
            draft.LastModifiedUtc = DateTime.UtcNow;

            var prepared = await PrepareSaveAsync(draft, cancellationToken).ConfigureAwait(false);

            if (!forceWrite &&
                !_requiresEnvelopeMigration &&
                string.Equals(prepared.Fingerprint, _lastSavedFingerprint, StringComparison.Ordinal))
            {
                return false;
            }

            // I blob precedono sempre l'envelope: dopo un arresto improvviso il
            // JSON non puo' puntare a un contenuto che non sia gia' atomico.
            await EnsureContentBlobsAsync(prepared.Blobs, cancellationToken).ConfigureAwait(false);

            string temporaryPath = _draftPath + ".tmp";
            await using (var stream = OpenWriteStream(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        prepared.Envelope,
                        JsonOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _draftPath, overwrite: true);
            _lastSavedFingerprint = prepared.Fingerprint;
            _requiresEnvelopeMigration = false;
            await CleanupUnreferencedContentAsync(
                    prepared.ReferencedContentKeys,
                    cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DeleteIfExistsAsync(_draftPath, cancellationToken).ConfigureAwait(false);
            await DeleteIfExistsAsync(_draftPath + ".tmp", cancellationToken).ConfigureAwait(false);
            await DeleteContentDirectoryAsync(cancellationToken).ConfigureAwait(false);
            ResetCachedState();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private Task<PreparedDraftSave> PrepareSaveAsync(
        QuoteHistoryEntry draft,
        CancellationToken cancellationToken) =>
        Task.Run(() => PrepareSave(draft, cancellationToken), cancellationToken);

    private PreparedDraftSave PrepareSave(
        QuoteHistoryEntry draft,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var blobs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        string? pdfContentKey = AddFileContent(draft.PdfFile, blobs, cancellationToken);
        var attachmentContentKeys = draft.Attachments
            .Select(file => AddFileContent(file, blobs, cancellationToken))
            .ToList();

        var envelope = new DraftEnvelope
        {
            Version = EnvelopeVersion,
            Draft = CreateEnvelopeDraftSnapshot(draft),
            PdfContentKey = pdfContentKey,
            AttachmentContentKeys = attachmentContentKeys
        };

        string fingerprint = ComputeStableFingerprint(
            draft,
            pdfContentKey,
            attachmentContentKeys,
            cancellationToken);
        var referencedContentKeys = new HashSet<string>(
            attachmentContentKeys.OfType<string>(),
            StringComparer.OrdinalIgnoreCase);
        if (pdfContentKey != null)
            referencedContentKeys.Add(pdfContentKey);

        return new PreparedDraftSave(
            fingerprint,
            envelope,
            blobs,
            referencedContentKeys);
    }

    private static QuoteHistoryEntry CreateEnvelopeDraftSnapshot(QuoteHistoryEntry draft) => new()
    {
        QuoteNumber = draft.QuoteNumber,
        Date = draft.Date,
        CustomerName = draft.CustomerName,
        CustomerSyncId = draft.CustomerSyncId,
        ReferenceName = draft.ReferenceName,
        ReferenceCustomerSyncId = draft.ReferenceCustomerSyncId,
        SiteName = draft.SiteName,
        BillingCustomerName = draft.BillingCustomerName,
        BillingCustomerSyncId = draft.BillingCustomerSyncId,
        PdfPath = draft.PdfPath,
        PaymentTerms = draft.PaymentTerms,
        IvaType = draft.IvaType,
        Notes = draft.Notes,
        Materials = draft.Materials,
        Labors = draft.Labors,
        Imponibile = draft.Imponibile,
        MaterialDiscount = draft.MaterialDiscount,
        LaborDiscount = draft.LaborDiscount,
        Total = draft.Total,
        Status = draft.Status,
        CreatedByDevice = draft.CreatedByDevice,
        LastModifiedByDevice = draft.LastModifiedByDevice,
        SentAtUtc = draft.SentAtUtc,
        SentMethod = draft.SentMethod,
        SentRecipient = draft.SentRecipient,
        SentByDevice = draft.SentByDevice,
        LastReminderAtUtc = draft.LastReminderAtUtc,
        ReminderCount = draft.ReminderCount,
        LastReminderByDevice = draft.LastReminderByDevice,
        Events = draft.Events,
        SupplierName = draft.SupplierName,
        MaterialOrderDate = draft.MaterialOrderDate,
        ExpectedDeliveryDate = draft.ExpectedDeliveryDate,
        MaterialStatus = draft.MaterialStatus,
        IsJointVenture = draft.IsJointVenture,
        PartnerCompanyName = draft.PartnerCompanyName,
        OurCosts = draft.OurCosts,
        PartnerCosts = draft.PartnerCosts,
        AdditionalCosts = draft.AdditionalCosts,
        PdfFile = CreateEnvelopeFileSnapshot(draft.PdfFile),
        Attachments = draft.Attachments.Select(CreateEnvelopeFileSnapshot).OfType<StoredFile>().ToList(),
        HasCompleteAttachmentSnapshot = draft.HasCompleteAttachmentSnapshot,
        LastModifiedUtc = draft.LastModifiedUtc,
        SyncHash = draft.SyncHash,
        BaseVersionUtc = draft.BaseVersionUtc,
        Revision = draft.Revision,
        BaseRevision = draft.BaseRevision,
        HasPendingDatabaseWrite = draft.HasPendingDatabaseWrite,
        IsEditingExistingQuoteDraft = draft.IsEditingExistingQuoteDraft,
        IsDraftQuoteNumberAllocated = draft.IsDraftQuoteNumberAllocated,
        WasCreatedByDraftAutosave = draft.WasCreatedByDraftAutosave,
        SharedDraftContentHash = draft.SharedDraftContentHash
    };

    private static StoredFile? CreateEnvelopeFileSnapshot(StoredFile? file) =>
        file == null
            ? null
            : new StoredFile
            {
                FileName = file.FileName,
                ContentType = file.ContentType,
                Content = [],
                ImportedAt = file.ImportedAt
            };

    private string? AddFileContent(
        StoredFile? file,
        IDictionary<string, byte[]> blobs,
        CancellationToken cancellationToken)
    {
        if (file == null)
            return null;

        byte[] content = file.Content ?? [];
        string key = GetContentKey(content, cancellationToken);
        blobs.TryAdd(key, content);
        return key;
    }

    private string GetContentKey(byte[] content, CancellationToken cancellationToken)
    {
        // Gli array SelectedAttachment.Content sono snapshot immutabili: quando
        // un allegato cambia, l'applicazione sostituisce l'array. La cache debole
        // evita di rileggere file grandi ad ogni autosave senza trattenerli in RAM.
        if (_contentKeyCache.TryGetValue(content, out var cached))
            return cached.Key;

        string key = ComputeContentKey(content, cancellationToken);
        CacheContentKey(content, key);
        return key;
    }

    private static string ComputeContentKey(byte[] content, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int offset = 0; offset < content.Length; offset += HashChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(HashChunkSize, content.Length - offset);
            hash.AppendData(content.AsSpan(offset, count));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private void CacheContentKey(byte[] content, string key)
    {
        _contentKeyCache.Remove(content);
        _contentKeyCache.Add(content, new CachedContentKey(key));
    }

    private static string ComputeStableFingerprint(
        QuoteHistoryEntry draft,
        string? pdfContentKey,
        IReadOnlyList<string?> attachmentContentKeys,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Date/LastModifiedUtc/ImportedAt e i metadati di revisione vengono
        // rigenerati dal salvataggio DB. Quando vanno persistiti il chiamante usa
        // forceWrite. Le chiavi SHA includono invece il contenuto reale dei file.
        var stablePayload = new
        {
            draft.QuoteNumber,
            draft.CustomerName,
            draft.CustomerSyncId,
            draft.ReferenceName,
            draft.ReferenceCustomerSyncId,
            draft.SiteName,
            draft.BillingCustomerName,
            draft.BillingCustomerSyncId,
            draft.PaymentTerms,
            draft.IvaType,
            Materials = draft.Materials.Select(item => new
            {
                item.PersistentId,
                item.Name,
                item.Description,
                item.UnitPrice,
                item.Quantity,
                item.Discount,
                item.IsSignificant,
                item.IsCompanyMaterial,
                item.SortOrder
            }).ToList(),
            Labors = draft.Labors.Select(item => new
            {
                item.PersistentId,
                item.Name,
                item.Description,
                item.UnitPrice,
                item.Quantity,
                item.Discount,
                item.IsSignificant,
                item.IsCompanyMaterial,
                item.SortOrder
            }).ToList(),
            draft.Imponibile,
            draft.MaterialDiscount,
            draft.LaborDiscount,
            draft.Total,
            draft.CreatedByDevice,
            draft.LastModifiedByDevice,
            draft.IsJointVenture,
            draft.PartnerCompanyName,
            OurCosts = draft.OurCosts.Select(cost => new { cost.Description, cost.Amount, cost.Notes }).ToList(),
            PartnerCosts = draft.PartnerCosts.Select(cost => new { cost.Description, cost.Amount, cost.Notes }).ToList(),
            AdditionalCosts = draft.AdditionalCosts.Select(cost => new { cost.Description, cost.Amount, cost.Notes }).ToList(),
            PdfFile = CreateStableFileMetadata(draft.PdfFile),
            PdfContentKey = pdfContentKey,
            Attachments = draft.Attachments.Select(CreateStableFileMetadata).ToList(),
            AttachmentContentKeys = attachmentContentKeys,
            draft.HasCompleteAttachmentSnapshot,
            draft.IsDraftQuoteNumberAllocated
        };

        byte[] metadataPayload = JsonSerializer.SerializeToUtf8Bytes(stablePayload);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(metadataPayload);
        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static StableFileMetadata? CreateStableFileMetadata(StoredFile? file) =>
        file == null
            ? null
            : new StableFileMetadata(file.FileName, file.ContentType);

    private async Task EnsureContentBlobsAsync(
        IReadOnlyDictionary<string, byte[]> blobs,
        CancellationToken cancellationToken)
    {
        if (blobs.Count == 0)
            return;

        Directory.CreateDirectory(_contentDirectory);
        foreach (var pair in blobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string targetPath = GetContentPath(pair.Key);
            if (File.Exists(targetPath))
                continue;

            string temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = OpenWriteStream(temporaryPath, createNew: true))
                {
                    byte[] content = pair.Value;
                    for (int offset = 0; offset < content.Length; offset += HashChunkSize)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int count = Math.Min(HashChunkSize, content.Length - offset);
                        await stream.WriteAsync(content.AsMemory(offset, count), cancellationToken)
                            .ConfigureAwait(false);
                    }

                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    File.Move(temporaryPath, targetPath, overwrite: false);
                }
                catch (IOException) when (File.Exists(targetPath))
                {
                    // Un altro writer ha pubblicato lo stesso contenuto.
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
    }

    private async Task HydrateEnvelopeContentAsync(
        DraftEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var draft = envelope.Draft!;
        if (envelope.AttachmentContentKeys.Count != draft.Attachments.Count)
        {
            throw new InvalidDataException(
                "Manifest allegati della bozza non coerente con i metadati.");
        }

        var loadedByKey = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (draft.PdfFile != null)
        {
            draft.PdfFile.Content = await ReadContentBlobAsync(
                    envelope.PdfContentKey,
                    loadedByKey,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        for (int index = 0; index < draft.Attachments.Count; index++)
        {
            draft.Attachments[index].Content = await ReadContentBlobAsync(
                    envelope.AttachmentContentKeys[index],
                    loadedByKey,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private Task CleanupUnreferencedContentAsync(
        IReadOnlySet<string> referencedContentKeys,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_contentDirectory) || cancellationToken.IsCancellationRequested)
            return Task.CompletedTask;

        try
        {
            foreach (string path in Directory.EnumerateFiles(
                _contentDirectory,
                "*" + ContentFileExtension,
                SearchOption.TopDirectoryOnly))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                string fileName = Path.GetFileName(path);
                string key = Path.GetFileNameWithoutExtension(fileName);
                if (!fileName.Equals(key + ContentFileExtension, StringComparison.OrdinalIgnoreCase) ||
                    !IsValidContentKey(key) ||
                    referencedContentKeys.Contains(key))
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Debug.WriteLine($"[Draft] Sidecar obsoleto non eliminato '{path}': {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[Draft] Pulizia sidecar non riuscita: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private async Task<byte[]> ReadContentBlobAsync(
        string? key,
        IDictionary<string, byte[]> loadedByKey,
        CancellationToken cancellationToken)
    {
        if (!IsValidContentKey(key))
            throw new InvalidDataException("Chiave contenuto allegato mancante o non valida.");

        if (loadedByKey.TryGetValue(key!, out var cached))
            return cached;

        string path = GetContentPath(key!);
        if (!File.Exists(path))
            throw new FileNotFoundException("Contenuto allegato della bozza non trovato.", path);

        byte[] content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        string actualKey = await Task.Run(
                () => ComputeContentKey(content, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(actualKey, key, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Contenuto allegato corrotto: {key}.");

        CacheContentKey(content, key!);
        loadedByKey[key!] = content;
        return content;
    }

    private static async Task<QuoteHistoryEntry?> DeserializeLegacyDraftAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var draft = document.RootElement.Deserialize<QuoteHistoryEntry>(JsonOptions);
        if (draft == null)
            return null;

        if (draft.PdfFile != null &&
            TryGetPropertyIgnoreCase(document.RootElement, "PdfFile", out var pdfElement))
        {
            draft.PdfFile.Content = ReadLegacyContent(pdfElement);
        }

        if (TryGetPropertyIgnoreCase(document.RootElement, "Attachments", out var attachmentsElement) &&
            attachmentsElement.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var fileElement in attachmentsElement.EnumerateArray())
            {
                if (index >= draft.Attachments.Count)
                    break;

                draft.Attachments[index].Content = ReadLegacyContent(fileElement);
                index++;
            }
        }

        return draft;
    }

    private static byte[] ReadLegacyContent(JsonElement fileElement)
    {
        if (!TryGetPropertyIgnoreCase(fileElement, "Content", out var contentElement) ||
            contentElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (contentElement.ValueKind == JsonValueKind.String)
            return contentElement.GetBytesFromBase64();

        if (contentElement.ValueKind == JsonValueKind.Array)
        {
            var content = new byte[contentElement.GetArrayLength()];
            int index = 0;
            foreach (var value in contentElement.EnumerateArray())
                content[index++] = value.GetByte();
            return content;
        }

        throw new JsonException("Formato legacy del contenuto allegato non supportato.");
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private string GetContentPath(string key)
    {
        if (!IsValidContentKey(key))
            throw new InvalidDataException("Chiave contenuto allegato non valida.");
        return Path.Combine(_contentDirectory, key + ContentFileExtension);
    }

    private static bool IsValidContentKey(string? key) =>
        key is { Length: 64 } && key.All(Uri.IsHexDigit);

    private static FileStream OpenReadStream(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        BufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static FileStream OpenWriteStream(string path, bool createNew = false) => new(
        path,
        createNew ? FileMode.CreateNew : FileMode.Create,
        FileAccess.Write,
        FileShare.None,
        BufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private void ResetCachedState()
    {
        _lastSavedFingerprint = null;
        _requiresEnvelopeMigration = false;
        _contentKeyCache.Clear();
    }

    private async Task DeleteContentDirectoryAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Directory.Exists(_contentDirectory))
                        Directory.Delete(_contentDirectory, recursive: true);
                }, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (attempt < 2)
            {
                await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(path))
                    File.Delete(path);

                return;
            }
            catch (IOException) when (attempt < 2)
            {
                await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (attempt < 2)
            {
                await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class DraftEnvelope
    {
        public int Version { get; set; }
        public QuoteHistoryEntry? Draft { get; set; }
        public string? PdfContentKey { get; set; }
        public List<string?> AttachmentContentKeys { get; set; } = [];
    }

    private sealed record PreparedDraftSave(
        string Fingerprint,
        DraftEnvelope Envelope,
        IReadOnlyDictionary<string, byte[]> Blobs,
        IReadOnlySet<string> ReferencedContentKeys);

    private sealed record StableFileMetadata(
        string FileName,
        string ContentType);

    private sealed record CachedContentKey(string Key);
}
