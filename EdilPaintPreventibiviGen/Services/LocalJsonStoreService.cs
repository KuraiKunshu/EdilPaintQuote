using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
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

            var json = await ReadTextWithBackupAsync(_historyPath, cancellationToken);
            return JsonSerializer.Deserialize<List<QuoteHistoryEntry>>(json, JsonOptions)
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
        await _historySemaphore.WaitAsync(cancellationToken);
        try
        {
            var history = await LoadHistoryInternalAsync(cancellationToken);
            var historyDict = history
                .GroupBy(q => q.QuoteNumber, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entriesToAddOrUpdate)
            {
                var localEntry = CreateLocalQuoteEntry(entry);
                // Preserva il timestamp originale quando l'entry arriva dal DB.
                if (localEntry.LastModifiedUtc == default)
                    localEntry.LastModifiedUtc = DateTime.UtcNow;

                // Ricalcola l'hash per riflettere i dati serializzati localmente.
                localEntry.SyncHash = ComputeQuoteHash(localEntry);

                historyDict[localEntry.QuoteNumber] = localEntry;
            }

            await SaveHistoryInternalAsync(historyDict.Values, cancellationToken);
            Debug.WriteLine($"[LocalJsonStore] BulkUpdate: {entriesToAddOrUpdate.Count()} quotes written");
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

    public async Task SaveOrUpdateQuoteAsync(QuoteHistoryEntry entry)
    {
        await _historySemaphore.WaitAsync();
        try
        {
            var history = await LoadHistoryInternalAsync();
            var localEntry = CreateLocalQuoteEntry(entry);
            var existing = history.FirstOrDefault(q =>
                q.QuoteNumber.Equals(localEntry.QuoteNumber, StringComparison.OrdinalIgnoreCase));

            if (localEntry.LastModifiedUtc == default)
                localEntry.LastModifiedUtc = DateTime.UtcNow;

            localEntry.SyncHash = ComputeQuoteHash(localEntry);

            if (existing != null)
                history.Remove(existing);

            history.Add(localEntry);
            await SaveHistoryInternalAsync(history);
        }
        finally
        {
            _historySemaphore.Release();
        }
    }

    public async Task DeleteQuoteAsync(string quoteNumber)
    {
        await _historySemaphore.WaitAsync();
        try
        {
            var history = await LoadHistoryInternalAsync();
            history.RemoveAll(q =>
                q.QuoteNumber.Equals(quoteNumber, StringComparison.OrdinalIgnoreCase));
            await SaveHistoryInternalAsync(history);
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

        var json = await ReadTextWithBackupAsync(_historyPath, cancellationToken);
        return JsonSerializer.Deserialize<List<QuoteHistoryEntry>>(json, JsonOptions)
               ?? new List<QuoteHistoryEntry>();
    }

    private async Task SaveHistoryInternalAsync(
        IEnumerable<QuoteHistoryEntry> entries,
        CancellationToken cancellationToken = default)
    {
        var localEntries = entries.Select(CreateLocalQuoteEntry).ToList();
        await WriteJsonWithBackupAsync(_historyPath, localEntries, cancellationToken);
    }
    #endregion

    #region Customers (Clienti)

    private async Task<List<Customer>> LoadCustomersInternalAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_customersPath))
            return new List<Customer>();

        var json = await ReadTextWithBackupAsync(_customersPath, cancellationToken);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("clienti", out var clientiArray))
            return new List<Customer>();

        var customers = new List<Customer>();
        foreach (var c in clientiArray.EnumerateArray())
            customers.Add(JsonSerializer.Deserialize<Customer>(c.GetRawText(), JsonOptions)!);

        return customers;
    }
    
    public async Task<List<Customer>> LoadCustomersAsync(CancellationToken cancellationToken = default)
    {
        await _customersSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_customersPath))
                return new List<Customer>();

            var json = await ReadTextWithBackupAsync(_customersPath, cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("clienti", out var clientiArray))
                return new List<Customer>();

            var customers = new List<Customer>();
            foreach (var c in clientiArray.EnumerateArray())
            {
                customers.Add(JsonSerializer.Deserialize<Customer>(c.GetRawText(), JsonOptions)!);
            }

            return customers;
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
            var wrapper = new { clienti = customers };
            await WriteJsonWithBackupAsync(_customersPath, wrapper);
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
        var wrapper = new { clienti = customers };
        await WriteJsonWithBackupAsync(_customersPath, wrapper, cancellationToken);
    }

    public async Task SaveOrUpdateCustomerAsync(Customer customer)
    {
        await _customersSemaphore.WaitAsync();
        try
        {
            // FIX: Usa il metodo INTERNAL che non acquisisce il semaforo
            var customers = await LoadCustomersInternalAsync();
            EnsureCustomerSyncId(customer);
            var existing = customers.FirstOrDefault(c => SameCustomer(c, customer));

            if (customer.LastModifiedUtc == default)
                customer.LastModifiedUtc = DateTime.UtcNow;

            if (existing != null)
                customers.Remove(existing);

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
            customers.RemoveAll(c =>
                SameCustomer(c, customer) ||
                c.BusinessName.Equals(originalBusinessName, StringComparison.OrdinalIgnoreCase));

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
        await _customersSemaphore.WaitAsync(cancellationToken);
        try
        {
            var existing = await LoadCustomersInternalAsync(cancellationToken);
            var dict = existing
                .GroupBy(CustomerKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var c in customersToAddOrUpdate)
            {
                EnsureCustomerSyncId(c);
                c.LastModifiedUtc = c.LastModifiedUtc == default ? DateTime.UtcNow : c.LastModifiedUtc;
                dict[CustomerKey(c)] = c;
            }

            await SaveCustomersInternalAsync(dict.Values, cancellationToken);
            Debug.WriteLine($"[LocalJsonStore] BulkUpdateCustomers: {customersToAddOrUpdate.Count()} customers written");
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
        var json = JsonSerializer.Serialize(value, JsonOptions);
        string temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);

        if (File.Exists(path))
            File.Copy(path, path + ".backup", overwrite: true);

        File.Move(temporaryPath, path, overwrite: true);
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
            customers.RemoveAll(customer => targets.Any(target => SameCustomer(customer, target)));
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
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            JsonDocument.Parse(json).Dispose();
            return json;
        }
        catch (JsonException) when (File.Exists(path + ".backup"))
        {
            string backup = await File.ReadAllTextAsync(path + ".backup", cancellationToken);
            JsonDocument.Parse(backup).Dispose();
            File.Copy(path + ".backup", path, overwrite: true);
            Debug.WriteLine($"[LocalJsonStore] Recuperato backup valido per {Path.GetFileName(path)}.");
            return backup;
        }
    }

    private static void EnsureCustomerSyncId(Customer customer)
    {
        if (customer.SyncId == Guid.Empty)
            customer.SyncId = Guid.NewGuid();
    }

    private static bool SameCustomer(Customer left, Customer right) =>
        (left.SyncId != Guid.Empty && right.SyncId != Guid.Empty && left.SyncId == right.SyncId) ||
        left.BusinessName.Equals(right.BusinessName, StringComparison.OrdinalIgnoreCase);

    private static string CustomerKey(Customer customer) =>
        customer.SyncId == Guid.Empty ? "name:" + customer.BusinessName : "id:" + customer.SyncId.ToString("N");

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

    private static QuoteHistoryEntry CreateLocalQuoteEntry(QuoteHistoryEntry entry)
    {
        return new QuoteHistoryEntry
        {
            QuoteNumber = entry.QuoteNumber,
            Date = entry.Date,
            CustomerName = entry.CustomerName,
            ReferenceName = entry.ReferenceName,
            PdfPath = entry.PdfPath,
            PaymentTerms = entry.PaymentTerms,
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
            IsJointVenture = entry.IsJointVenture,
            PartnerCompanyName = entry.PartnerCompanyName,
            OurCosts = entry.OurCosts,
            PartnerCosts = entry.PartnerCosts,
            AdditionalCosts = entry.AdditionalCosts,
            LastModifiedUtc = entry.LastModifiedUtc,
            BaseVersionUtc = entry.BaseVersionUtc,
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
            }).ToList()
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

