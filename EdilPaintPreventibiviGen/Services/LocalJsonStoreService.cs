using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public class LocalJsonStoreService
{
    private readonly string _historyPath;
    private readonly string _customersPath;
    private readonly string _companyPath;
    private readonly string _laborCatalogPath;
    private readonly string _personalMaterialsPath;
    private readonly string _conflictsPath;
    private readonly SemaphoreSlim _historySemaphore = new(1, 1);
    private readonly SemaphoreSlim _customersSemaphore = new(1, 1);
    private readonly SemaphoreSlim _companySemaphore = new(1, 1);
    private readonly SemaphoreSlim _catalogSemaphore = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public LocalJsonStoreService(string assetsPath)
    {
        _historyPath = Path.Combine(assetsPath, "history.json");
        _customersPath = Path.Combine(assetsPath, "clienti.json");
        _companyPath = Path.Combine(assetsPath, "azienda.json");
        _laborCatalogPath = Path.Combine(assetsPath, "dati_lavori.json");
        _personalMaterialsPath = Path.Combine(assetsPath, "materiali_personali.json");
        _conflictsPath = Path.Combine(assetsPath, "Conflicts");

        Directory.CreateDirectory(assetsPath);
        Directory.CreateDirectory(_conflictsPath);
    }

    #region Company and Catalogs

    public async Task<Company?> LoadCompanyAsync()
    {
        await _companySemaphore.WaitAsync();
        try
        {
            if (!File.Exists(_companyPath))
                return null;

            var json = await ReadTextWithBackupAsync(_companyPath);
            return JsonSerializer.Deserialize<Company>(json, JsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalJsonStore] Error loading company: {ex.Message}");
            return null;
        }
        finally
        {
            _companySemaphore.Release();
        }
    }

    public async Task SaveCompanyAsync(Company company, string selectedLogo)
    {
        await _companySemaphore.WaitAsync();
        try
        {
            if (!string.IsNullOrWhiteSpace(selectedLogo))
            {
                int selectedIndex = company.Logo.FindIndex(logo =>
                    Path.GetFileName(logo).Equals(selectedLogo, StringComparison.OrdinalIgnoreCase));

                if (selectedIndex >= 0)
                    company.Logo_index = selectedIndex;
            }

            await WriteJsonWithBackupAsync(_companyPath, company);
        }
        finally
        {
            _companySemaphore.Release();
        }
    }

    public async Task<List<Item>> LoadLaborCatalogAsync()
    {
        await _catalogSemaphore.WaitAsync();
        try
        {
            if (!File.Exists(_laborCatalogPath))
                return new List<Item>();

            var json = await ReadTextWithBackupAsync(_laborCatalogPath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                return JsonSerializer.Deserialize<List<Item>>(json, JsonOptions) ?? new List<Item>();

            if (!doc.RootElement.TryGetProperty("lavori", out var lavoriArray))
                return new List<Item>();

            var labors = new List<Item>();
            foreach (var e in lavoriArray.EnumerateArray())
            {
                labors.Add(new Item
                {
                    PersistentId = GetJsonInt(
                        e,
                        "persistentId",
                        "PersistentId",
                        "catalogItemId",
                        "CatalogItemId",
                        "idCatalogo",
                        "IdCatalogo"),
                    Name = GetJsonString(e, "nome", "Nome", "name", "Name"),
                    Description = GetJsonString(e, "descrizione", "Descrizione", "description", "Description"),
                    UnitPrice = GetJsonDouble(e, "valore", "Valore", "unitPrice", "UnitPrice"),
                    Quantity = 1
                });
            }

            return labors;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalJsonStore] Error loading labor catalog: {ex.Message}");
            return new List<Item>();
        }
        finally
        {
            _catalogSemaphore.Release();
        }
    }

    public async Task SaveLaborCatalogAsync(IEnumerable<Item> labors)
    {
        await _catalogSemaphore.WaitAsync();
        try
        {
            var wrapper = new
            {
                lavori = labors.Select(l => new
                {
                    persistentId = l.PersistentId,
                    nome = l.Name,
                    descrizione = l.Description,
                    valore = l.UnitPrice
                }).ToList()
            };

            await WriteJsonWithBackupAsync(_laborCatalogPath, wrapper);
        }
        finally
        {
            _catalogSemaphore.Release();
        }
    }

    public async Task<List<Item>> LoadPersonalMaterialsAsync()
    {
        await _catalogSemaphore.WaitAsync();
        try
        {
            if (!File.Exists(_personalMaterialsPath))
                return new List<Item>();

            var json = await ReadTextWithBackupAsync(_personalMaterialsPath);
            return JsonSerializer.Deserialize<List<Item>>(json, JsonOptions) ?? new List<Item>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalJsonStore] Error loading personal materials: {ex.Message}");
            return new List<Item>();
        }
        finally
        {
            _catalogSemaphore.Release();
        }
    }

    public async Task SavePersonalMaterialsAsync(IEnumerable<Item> materials)
    {
        await _catalogSemaphore.WaitAsync();
        try
        {
            await WriteJsonWithBackupAsync(_personalMaterialsPath, materials.ToList());
        }
        finally
        {
            _catalogSemaphore.Release();
        }
    }

    #endregion

    #region History (Storico Preventivi)

    public async Task<List<QuoteHistoryEntry>> LoadHistoryAsync(CancellationToken cancellationToken = default)
    {
        await _historySemaphore.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_historyPath))
                return new List<QuoteHistoryEntry>();

            return await ReadJsonWithBackupAsync<List<QuoteHistoryEntry>>(
                       _historyPath,
                       cancellationToken)
                   ?? new List<QuoteHistoryEntry>();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalJsonStore] Error loading history: {ex.Message}");
            return new List<QuoteHistoryEntry>();
        }
        finally
        {
            _historySemaphore.Release();
        }
    }

    public async Task BulkUpdateQuotesAsync(
        IEnumerable<QuoteHistoryEntry> entriesToAddOrUpdate,
        CancellationToken cancellationToken = default)
    {
        await _historySemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var history = await LoadHistoryInternalAsync(cancellationToken).ConfigureAwait(false);
            var updates = entriesToAddOrUpdate.ToList();
            var mergedHistory = await Task.Run(() =>
            {
                var historyDict = history
                    .GroupBy(q => q.QuoteNumber, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                foreach (var entry in updates)
                {
                    var localEntry = CreateLocalQuoteEntry(entry);
                    // Preserva il timestamp originale quando l'entry arriva dal DB.
                    if (localEntry.LastModifiedUtc == default)
                        localEntry.LastModifiedUtc = DateTime.UtcNow;

                    // Ricalcola l'hash per riflettere i dati serializzati localmente.
                    localEntry.SyncHash = ComputeQuoteHash(localEntry);

                    historyDict[localEntry.QuoteNumber] = localEntry;
                }

                return historyDict.Values.ToList();
            }, cancellationToken).ConfigureAwait(false);

            await SaveHistoryInternalAsync(mergedHistory, cancellationToken).ConfigureAwait(false);
            Debug.WriteLine($"[LocalJsonStore] BulkUpdate: {updates.Count} quotes written");
        }
        finally
        {
            _historySemaphore.Release();
        }
    }
    
    public async Task<QuoteHistoryEntry?> GetQuoteByNumberAsync(string quoteNumber)
    {
        var history = await LoadHistoryAsync();
        return history.FirstOrDefault(q =>
            q.QuoteNumber.Equals(quoteNumber, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SaveOrUpdateQuoteAsync(
        QuoteHistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        await _historySemaphore.WaitAsync(cancellationToken);
        try
        {
            var history = await LoadHistoryInternalAsync(cancellationToken);
            var localEntry = CreateLocalQuoteEntry(entry);
            var existing = history.FirstOrDefault(q =>
                q.QuoteNumber.Equals(localEntry.QuoteNumber, StringComparison.OrdinalIgnoreCase));

            if (localEntry.LastModifiedUtc == default)
                localEntry.LastModifiedUtc = DateTime.UtcNow;

            localEntry.SyncHash = ComputeQuoteHash(localEntry);

            if (existing != null)
                history.Remove(existing);

            history.Add(localEntry);
            await SaveHistoryInternalAsync(history, cancellationToken);
        }
        finally
        {
            _historySemaphore.Release();
        }
    }

    public async Task DeleteQuoteAsync(
        string quoteNumber,
        CancellationToken cancellationToken = default)
    {
        await _historySemaphore.WaitAsync(cancellationToken);
        try
        {
            var history = await LoadHistoryInternalAsync(cancellationToken);
            history.RemoveAll(q =>
                q.QuoteNumber.Equals(quoteNumber, StringComparison.OrdinalIgnoreCase));
            await SaveHistoryInternalAsync(history, cancellationToken);
        }
        finally
        {
            _historySemaphore.Release();
        }
    }

    public async Task DeleteQuotesAsync(
        IEnumerable<string> quoteNumbers,
        CancellationToken cancellationToken = default)
    {
        var numberSet = new HashSet<string>(quoteNumbers, StringComparer.OrdinalIgnoreCase);
        if (numberSet.Count == 0)
            return;

        await _historySemaphore.WaitAsync(cancellationToken);
        try
        {
            var history = await LoadHistoryInternalAsync(cancellationToken);
            if (history.RemoveAll(q => numberSet.Contains(q.QuoteNumber)) > 0)
                await SaveHistoryInternalAsync(history, cancellationToken);
        }
        finally
        {
            _historySemaphore.Release();
        }
    }

    public Task<QuoteHistoryEntry?> UpdateQuoteNotesAsync(string quoteNumber, string notes) =>
        UpdateQuoteMetadataAsync(quoteNumber, quote =>
        {
            quote.Notes = notes;
            quote.LastModifiedByDevice = DeviceNameService.GetCurrentDeviceName();
            AddEvent(quote, "note", string.IsNullOrWhiteSpace(notes) ? "Note svuotate" : "Note aggiornate");
        });

    public Task<QuoteHistoryEntry?> UpdateQuoteStatusAsync(string quoteNumber, QuoteStatus status) =>
        UpdateQuoteMetadataAsync(quoteNumber, quote =>
        {
            quote.Status = status;
            quote.LastModifiedByDevice = DeviceNameService.GetCurrentDeviceName();
            AddEvent(quote, "stato", $"Stato aggiornato: {status}");
        });

    public Task<QuoteHistoryEntry?> UpdateQuoteSendInfoAsync(string quoteNumber, QuoteSendInfo sendInfo) =>
        UpdateQuoteMetadataAsync(quoteNumber, quote =>
        {
            string deviceName = string.IsNullOrWhiteSpace(sendInfo.DeviceName)
                ? DeviceNameService.GetCurrentDeviceName()
                : sendInfo.DeviceName.Trim();

            quote.Status = QuoteStatus.Spedito;
            quote.SentAtUtc = sendInfo.SentAtUtc == default ? DateTime.UtcNow : sendInfo.SentAtUtc;
            quote.SentMethod = sendInfo.Method?.Trim() ?? string.Empty;
            quote.SentRecipient = sendInfo.Recipient?.Trim() ?? string.Empty;
            quote.SentByDevice = deviceName;
            quote.LastModifiedByDevice = deviceName;
            AddEvent(quote, "invio", $"Preventivo inviato tramite {quote.SentMethod}".Trim(), deviceName, quote.SentAtUtc);
        });

    public Task<QuoteHistoryEntry?> RegisterQuoteReminderAsync(string quoteNumber, QuoteReminderInfo reminderInfo) =>
        UpdateQuoteMetadataAsync(quoteNumber, quote =>
        {
            string deviceName = string.IsNullOrWhiteSpace(reminderInfo.DeviceName)
                ? DeviceNameService.GetCurrentDeviceName()
                : reminderInfo.DeviceName.Trim();

            quote.Status = QuoteStatus.Spedito;
            quote.LastReminderAtUtc = reminderInfo.ReminderAtUtc == default ? DateTime.UtcNow : reminderInfo.ReminderAtUtc;
            quote.ReminderCount += 1;
            quote.LastReminderByDevice = deviceName;
            quote.LastModifiedByDevice = deviceName;
            AddEvent(quote, "sollecito", $"Sollecito registrato (n. {quote.ReminderCount})", deviceName, quote.LastReminderAtUtc);
        });

    public Task<QuoteHistoryEntry?> UpdateQuoteSupplierInfoAsync(string quoteNumber, QuoteSupplierInfo supplierInfo) =>
        UpdateQuoteMetadataAsync(quoteNumber, quote =>
        {
            string deviceName = string.IsNullOrWhiteSpace(supplierInfo.DeviceName)
                ? DeviceNameService.GetCurrentDeviceName()
                : supplierInfo.DeviceName.Trim();

            quote.SupplierName = supplierInfo.SupplierName?.Trim() ?? string.Empty;
            quote.MaterialOrderDate = supplierInfo.MaterialOrderDate;
            quote.ExpectedDeliveryDate = supplierInfo.ExpectedDeliveryDate;
            quote.MaterialStatus = supplierInfo.MaterialStatus?.Trim() ?? string.Empty;
            quote.LastModifiedByDevice = deviceName;
            AddEvent(quote, "fornitori", "Dati fornitori aggiornati", deviceName);
        });

    public async Task ArchiveQuoteConflictAsync(
        QuoteHistoryEntry entry,
        string reason,
        CancellationToken cancellationToken = default)
    {
        string safeQuoteNumber = string.Concat(entry.QuoteNumber.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string path = Path.Combine(
            _conflictsPath,
            $"{DateTime.UtcNow:yyyyMMdd_HHmmssfff}_{safeQuoteNumber}.json");
        await WriteJsonWithBackupAsync(path, new { Reason = reason, Quote = CreateLocalQuoteEntry(entry) }, cancellationToken);
    }

    private async Task<QuoteHistoryEntry?> UpdateQuoteMetadataAsync(
        string quoteNumber,
        Action<QuoteHistoryEntry> update)
    {
        await _historySemaphore.WaitAsync();
        try
        {
            var history = await LoadHistoryInternalAsync();
            var entry = history.FirstOrDefault(q =>
                q.QuoteNumber.Equals(quoteNumber, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                return null;

            update(entry);
            entry.LastModifiedUtc = DateTime.UtcNow;
            entry.SyncHash = ComputeQuoteHash(entry);
            await SaveHistoryInternalAsync(history);
            return entry;
        }
        finally
        {
            _historySemaphore.Release();
        }
    }

    private async Task<List<QuoteHistoryEntry>> LoadHistoryInternalAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_historyPath))
            return new List<QuoteHistoryEntry>();

        return await ReadJsonWithBackupAsync<List<QuoteHistoryEntry>>(
                   _historyPath,
                   cancellationToken).ConfigureAwait(false)
               ?? new List<QuoteHistoryEntry>();
    }

    private async Task SaveHistoryInternalAsync(
        IEnumerable<QuoteHistoryEntry> entries,
        CancellationToken cancellationToken = default)
    {
        // Materializziamo solo i riferimenti prima di cedere il controllo; la
        // copia completa e la serializzazione dello storico possono essere
        // costose e devono restare fuori dal Dispatcher.
        var entriesSnapshot = entries.ToList();
        var localEntries = await Task.Run(
                () => entriesSnapshot.Select(CreateLocalQuoteEntry).ToList(),
                cancellationToken)
            .ConfigureAwait(false);
        await WriteJsonWithBackupAsync(_historyPath, localEntries, cancellationToken)
            .ConfigureAwait(false);
    }
    #endregion

    #region Customers (Clienti)

    private async Task<List<Customer>> LoadCustomersInternalAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_customersPath))
            return new List<Customer>();

        var wrapper = await ReadJsonWithBackupAsync<CustomerFileWrapper>(
            _customersPath,
            cancellationToken).ConfigureAwait(false);
        return wrapper?.Customers ?? new List<Customer>();
    }
    
    public async Task<List<Customer>> LoadCustomersAsync(CancellationToken cancellationToken = default)
    {
        await _customersSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_customersPath))
                return new List<Customer>();

            var wrapper = await ReadJsonWithBackupAsync<CustomerFileWrapper>(
                _customersPath,
                cancellationToken);
            return wrapper?.Customers ?? new List<Customer>();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalJsonStore] Error loading customers: {ex.Message}");
            return new List<Customer>();
        }
        finally
        {
            _customersSemaphore.Release();
        }
    }

    public async Task SaveCustomersAsync(IEnumerable<Customer> customers)
    {
        await _customersSemaphore.WaitAsync();
        try
        {
            var wrapper = new { clienti = customers.ToList() };
            await WriteJsonWithBackupAsync(_customersPath, wrapper).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalJsonStore] Error saving customers: {ex.Message}");
            throw;
        }
        finally
        {
            _customersSemaphore.Release();
        }
    }
    
    private async Task SaveCustomersInternalAsync(
        IEnumerable<Customer> customers,
        CancellationToken cancellationToken = default)
    {
        var wrapper = new { clienti = customers.ToList() };
        await WriteJsonWithBackupAsync(_customersPath, wrapper, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveOrUpdateCustomerAsync(Customer customer)
    {
        await _customersSemaphore.WaitAsync();
        try
        {
            // FIX: Usa il metodo INTERNAL che non acquisisce il semaforo
            var customers = await LoadCustomersInternalAsync();
            EnsureCustomerSyncId(customer);
            if (customer.LastModifiedUtc == default)
                customer.LastModifiedUtc = DateTime.UtcNow;

            RemoveMatchingCustomerAliases(customers, customer);
            customers.Add(customer);
            await SaveCustomersInternalAsync(customers);
        }
        finally
        {
            _customersSemaphore.Release();
        }
    }

    public async Task UpdateCustomerAsync(string originalBusinessName, Customer customer)
    {
        await _customersSemaphore.WaitAsync();
        try
        {
            var customers = await LoadCustomersInternalAsync();
            EnsureCustomerSyncId(customer);
            RemoveMatchingCustomerAliases(customers, customer);

            var originalLegacyAliases = customers
                .Select((current, index) => (current, index))
                .Where(entry =>
                    entry.current.SyncId == Guid.Empty &&
                    entry.current.BusinessName.Equals(originalBusinessName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (originalLegacyAliases.Count == 1)
                customers.RemoveAt(originalLegacyAliases[0].index);

            if (customer.LastModifiedUtc == default)
                customer.LastModifiedUtc = DateTime.UtcNow;
            customers.Add(customer);
            await SaveCustomersInternalAsync(customers);
        }
        finally
        {
            _customersSemaphore.Release();
        }
    }
    
    public async Task BulkUpdateCustomersAsync(
        IEnumerable<Customer> customersToAddOrUpdate,
        CancellationToken cancellationToken = default)
    {
        await _customersSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await LoadCustomersInternalAsync(cancellationToken).ConfigureAwait(false);
            var updates = customersToAddOrUpdate.ToList();
            await Task.Run(() =>
            {
                var stableIdsByBusinessName = existing
                    .Concat(updates)
                    .Where(customer => customer.SyncId != Guid.Empty)
                    .GroupBy(customer => customer.BusinessName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(customer => customer.SyncId).Distinct().Count(),
                        StringComparer.OrdinalIgnoreCase);

                foreach (var customer in updates)
                {
                    EnsureCustomerSyncId(customer);
                    if (customer.LastModifiedUtc == default)
                        customer.LastModifiedUtc = DateTime.UtcNow;

                    // Un ID stabile identifica il record. Il nome viene usato solo
                    // per rimuovere una vecchia copia legacy ancora priva di ID:
                    // due clienti omonimi con ID diversi devono restare distinti.
                    bool hasAmbiguousStableName =
                        stableIdsByBusinessName.TryGetValue(customer.BusinessName, out int stableIdCount) &&
                        stableIdCount > 1;
                    RemoveMatchingCustomerAliases(
                        existing,
                        customer,
                        allowSingleNameFallback: !hasAmbiguousStableName);
                    existing.Add(customer);
                }
            }, cancellationToken).ConfigureAwait(false);

            await SaveCustomersInternalAsync(existing, cancellationToken).ConfigureAwait(false);
            Debug.WriteLine($"[LocalJsonStore] BulkUpdateCustomers: {updates.Count} customers written");
        }
        finally
        {
            _customersSemaphore.Release();
        }
    }
    
    public async Task DeleteCustomerAsync(Customer customer)
    {
        await _customersSemaphore.WaitAsync();
        try
        {
            var customers = await LoadCustomersInternalAsync();
            customers.RemoveAll(c => SameCustomer(c, customer));
            await SaveCustomersInternalAsync(customers);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalJsonStore] Error deleting customer: {ex.Message}");
            throw;
        }
        finally
        {
            _customersSemaphore.Release();
        }
    }

    #endregion

    #region Utilities

    private static async Task WriteJsonWithBackupAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken = default)
    {
        string temporaryPath = path + ".tmp";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await JsonSerializer.SerializeAsync(
                    stream,
                    value,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        // Una volta completato il file temporaneo, backup e sostituzione devono
        // restare una singola sequenza non interrompibile. Il semaforo del
        // chiamante impedisce scritture concorrenti sullo stesso archivio.
        await Task.Run(() =>
        {
            if (File.Exists(path))
                File.Copy(path, path + ".backup", overwrite: true);

            File.Move(temporaryPath, path, overwrite: true);
        }, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task DeleteCustomersAsync(
        IEnumerable<Customer> customersToDelete,
        CancellationToken cancellationToken = default)
    {
        var targets = customersToDelete.ToList();
        if (targets.Count == 0)
            return;

        await _customersSemaphore.WaitAsync(cancellationToken);
        try
        {
            var customers = await LoadCustomersInternalAsync(cancellationToken);
            var targetIds = targets
                .Select(target => target.SyncId)
                .Where(syncId => syncId != Guid.Empty)
                .ToHashSet();
            var legacyTargetNames = targets
                .Where(target => target.SyncId == Guid.Empty)
                .Select(target => target.BusinessName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            customers.RemoveAll(customer => customer.SyncId != Guid.Empty
                ? targetIds.Contains(customer.SyncId)
                : legacyTargetNames.Contains(customer.BusinessName));
            await SaveCustomersInternalAsync(customers, cancellationToken);
        }
        finally
        {
            _customersSemaphore.Release();
        }
    }

    private static async Task<string> ReadTextWithBackupAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken)
                .ConfigureAwait(false);
            await Task.Run(() => JsonDocument.Parse(json).Dispose(), cancellationToken)
                .ConfigureAwait(false);
            return json;
        }
        catch (JsonException) when (File.Exists(path + ".backup"))
        {
            string backup = await File.ReadAllTextAsync(path + ".backup", cancellationToken)
                .ConfigureAwait(false);
            await Task.Run(() => JsonDocument.Parse(backup).Dispose(), cancellationToken)
                .ConfigureAwait(false);
            await Task.Run(
                    () => File.Copy(path + ".backup", path, overwrite: true),
                    CancellationToken.None)
                .ConfigureAwait(false);
            Debug.WriteLine($"[LocalJsonStore] Recuperato backup valido per {Path.GetFileName(path)}.");
            return backup;
        }
    }

    private static async Task<T?> ReadJsonWithBackupAsync<T>(
        string path,
        CancellationToken cancellationToken = default)
    {
        static async Task<T?> DeserializeFileAsync(string filePath, CancellationToken token)
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, token)
                .ConfigureAwait(false);
        }

        try
        {
            return await DeserializeFileAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException) when (File.Exists(path + ".backup"))
        {
            var restored = await DeserializeFileAsync(path + ".backup", cancellationToken)
                .ConfigureAwait(false);
            await Task.Run(
                    () => File.Copy(path + ".backup", path, overwrite: true),
                    CancellationToken.None)
                .ConfigureAwait(false);
            Debug.WriteLine($"[LocalJsonStore] Recuperato backup valido per {Path.GetFileName(path)}.");
            return restored;
        }
    }

    private static void EnsureCustomerSyncId(Customer customer)
    {
        if (customer.SyncId == Guid.Empty)
            customer.SyncId = Guid.NewGuid();
    }

    private static bool SameCustomer(Customer left, Customer right)
    {
        if (left.SyncId != Guid.Empty || right.SyncId != Guid.Empty)
            return left.SyncId != Guid.Empty &&
                   right.SyncId != Guid.Empty &&
                   left.SyncId == right.SyncId;

        return left.BusinessName.Equals(right.BusinessName, StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveMatchingCustomerAliases(
        List<Customer> customers,
        Customer incoming,
        bool allowSingleNameFallback = true)
    {
        customers.RemoveAll(current =>
            current.SyncId != Guid.Empty && current.SyncId == incoming.SyncId);

        var legacyAliases = customers
            .Select((customer, index) => (customer, index))
            .Where(entry =>
                entry.customer.SyncId == Guid.Empty &&
                entry.customer.BusinessName.Equals(incoming.BusinessName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (legacyAliases.Count == 0)
            return;

        var exactAlias = legacyAliases.FirstOrDefault(entry =>
            CustomerAliasesHaveSameContent(entry.customer, incoming));
        int indexToRemove = exactAlias.customer != null
            ? exactAlias.index
            : allowSingleNameFallback && legacyAliases.Count == 1
                ? legacyAliases[0].index
                : -1;
        if (indexToRemove >= 0)
            customers.RemoveAt(indexToRemove);
    }

    private static bool CustomerAliasesHaveSameContent(Customer left, Customer right) =>
        string.Equals(left.BusinessName, right.BusinessName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Address, right.Address, StringComparison.Ordinal) &&
        string.Equals(left.Email, right.Email, StringComparison.Ordinal) &&
        string.Equals(left.Phone, right.Phone, StringComparison.Ordinal) &&
        left.MaterialDiscount.Equals(right.MaterialDiscount) &&
        left.LaborDiscount.Equals(right.LaborDiscount) &&
        left.SupplierDiscount.Equals(right.SupplierDiscount) &&
        left.IsSupplier == right.IsSupplier &&
        left.LastModifiedUtc == right.LastModifiedUtc &&
        left.BaseVersionUtc == right.BaseVersionUtc &&
        left.HasPendingDatabaseWrite == right.HasPendingDatabaseWrite;

    private sealed class CustomerFileWrapper
    {
        [JsonPropertyName("clienti")]
        public List<Customer> Customers { get; init; } = [];
    }

    private static string GetJsonString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static double GetJsonDouble(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (!element.TryGetProperty(name, out var prop))
                continue;

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var value))
                return value;

            if (prop.ValueKind == JsonValueKind.String &&
                double.TryParse(prop.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return value;
        }

        return 0;
    }

    private static int GetJsonInt(JsonElement element, params string[] propertyNames)
    {
        foreach (string name in propertyNames)
        {
            if (!element.TryGetProperty(name, out JsonElement property))
                continue;

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value))
                return value;

            if (property.ValueKind == JsonValueKind.String &&
                int.TryParse(
                    property.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value))
            {
                return value;
            }
        }

        return 0;
    }

    private static QuoteHistoryEntry CreateLocalQuoteEntry(QuoteHistoryEntry entry)
    {
        return new QuoteHistoryEntry
        {
            QuoteNumber = entry.QuoteNumber,
            Date = entry.Date,
            CustomerName = entry.CustomerName,
            CustomerSyncId = entry.CustomerSyncId,
            ReferenceName = entry.ReferenceName,
            ReferenceCustomerSyncId = entry.ReferenceCustomerSyncId,
            SiteName = entry.SiteName,
            BillingCustomerName = entry.BillingCustomerName,
            BillingCustomerSyncId = entry.BillingCustomerSyncId,
            PdfPath = entry.PdfPath,
            PaymentTerms = entry.PaymentTerms,
            CustomerNotes = entry.CustomerNotes,
            IvaType = entry.IvaType,
            Notes = entry.Notes,
            Materials = entry.Materials,
            Labors = entry.Labors,
            Imponibile = entry.Imponibile,
            MaterialDiscount = entry.MaterialDiscount,
            LaborDiscount = entry.LaborDiscount,
            Total = entry.Total,
            Status = entry.Status,
            CreatedByDevice = entry.CreatedByDevice,
            LastModifiedByDevice = entry.LastModifiedByDevice,
            SentAtUtc = entry.SentAtUtc,
            SentMethod = entry.SentMethod,
            SentRecipient = entry.SentRecipient,
            SentByDevice = entry.SentByDevice,
            LastReminderAtUtc = entry.LastReminderAtUtc,
            ReminderCount = entry.ReminderCount,
            LastReminderByDevice = entry.LastReminderByDevice,
            Events = entry.Events.ToList(),
            SupplierName = entry.SupplierName,
            MaterialOrderDate = entry.MaterialOrderDate,
            ExpectedDeliveryDate = entry.ExpectedDeliveryDate,
            MaterialStatus = entry.MaterialStatus,
            IsJointVenture = entry.IsJointVenture,
            PartnerCompanyName = entry.PartnerCompanyName,
            OurCosts = entry.OurCosts,
            PartnerCosts = entry.PartnerCosts,
            AdditionalCosts = entry.AdditionalCosts,
            LastModifiedUtc = entry.LastModifiedUtc,
            BaseVersionUtc = entry.BaseVersionUtc,
            Revision = entry.Revision,
            BaseRevision = entry.BaseRevision,
            HasPendingDatabaseWrite = entry.HasPendingDatabaseWrite,
            IsEditingExistingQuoteDraft = entry.IsEditingExistingQuoteDraft,
            SyncHash = entry.SyncHash,
            PdfFile = entry.PdfFile == null ? null : new StoredFile
            {
                FileName = entry.PdfFile.FileName,
                ContentType = entry.PdfFile.ContentType,
                Content = [],
                ImportedAt = entry.PdfFile.ImportedAt
            },
            Attachments = entry.Attachments.Select(a => new StoredFile
            {
                FileName = a.FileName,
                ContentType = a.ContentType,
                Content = [],
                ImportedAt = a.ImportedAt
            }).ToList(),
            // I byte vengono omessi dalla cache locale: non dichiarare uno
            // snapshot completo, altrimenti una sync futura potrebbe cancellare
            // gli allegati autorevoli presenti nel database.
            HasCompleteAttachmentSnapshot = false
        };
    }

    private static string ComputeQuoteHash(QuoteHistoryEntry entry)
    {
        return QuoteSyncHashService.Compute(entry);
    }

    private static void AddEvent(
        QuoteHistoryEntry quote,
        string eventType,
        string description,
        string? deviceName = null,
        DateTime? createdAtUtc = null)
    {
        quote.Events.Add(new QuoteEventEntry
        {
            CreatedAtUtc = (createdAtUtc ?? DateTime.UtcNow).ToUniversalTime(),
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? DeviceNameService.GetCurrentDeviceName() : deviceName.Trim(),
            EventType = eventType,
            Description = description
        });
    }

    #endregion
}

