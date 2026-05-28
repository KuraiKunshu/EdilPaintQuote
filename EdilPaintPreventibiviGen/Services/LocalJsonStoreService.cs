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

        Directory.CreateDirectory(assetsPath);
    }

    #region Company and Catalogs

    public async Task<Company?> LoadCompanyAsync()
    {
        await _companySemaphore.WaitAsync();
        try
        {
            if (!File.Exists(_companyPath))
                return null;

            var json = await File.ReadAllTextAsync(_companyPath);
            return JsonSerializer.Deserialize<Company>(json, JsonOptions);
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

            var json = await File.ReadAllTextAsync(_laborCatalogPath);
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

            var json = await File.ReadAllTextAsync(_personalMaterialsPath);
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

    public async Task<List<QuoteHistoryEntry>> LoadHistoryAsync()
    {
        await _historySemaphore.WaitAsync();
        try
        {
            if (!File.Exists(_historyPath))
                return new List<QuoteHistoryEntry>();

            var json = await File.ReadAllTextAsync(_historyPath);
            return JsonSerializer.Deserialize<List<QuoteHistoryEntry>>(json, JsonOptions)
                   ?? new List<QuoteHistoryEntry>();
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

    public async Task BulkUpdateQuotesAsync(IEnumerable<QuoteHistoryEntry> entriesToAddOrUpdate)
    {
        await _historySemaphore.WaitAsync();
        try
        {
            var history = await LoadHistoryInternalAsync();
            var historyDict = history
                .GroupBy(q => q.QuoteNumber, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entriesToAddOrUpdate)
            {
                var localEntry = CreateLocalQuoteEntry(entry);
                // NON aggiornare LastModifiedUtc se l'entry viene dal DB â€” preserva il timestamp originale
                if (localEntry.LastModifiedUtc == default)
                    localEntry.LastModifiedUtc = DateTime.UtcNow;

                // Ricalcola l'hash solo se non Ã¨ giÃ  presente (il DB lo porta giÃ  aggiornato)
                localEntry.SyncHash = ComputeQuoteHash(localEntry);

                historyDict[localEntry.QuoteNumber] = localEntry;
            }

            await SaveHistoryInternalAsync(historyDict.Values);
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

    private async Task<List<QuoteHistoryEntry>> LoadHistoryInternalAsync()
    {
        if (!File.Exists(_historyPath))
            return new List<QuoteHistoryEntry>();

        var json = await File.ReadAllTextAsync(_historyPath);
        return JsonSerializer.Deserialize<List<QuoteHistoryEntry>>(json, JsonOptions)
               ?? new List<QuoteHistoryEntry>();
    }

    private async Task SaveHistoryInternalAsync(IEnumerable<QuoteHistoryEntry> entries)
    {
        if (File.Exists(_historyPath))
            File.Copy(_historyPath, _historyPath + ".backup", overwrite: true);

        var localEntries = entries.Select(CreateLocalQuoteEntry).ToList();
        var json = JsonSerializer.Serialize(localEntries, JsonOptions);
        await File.WriteAllTextAsync(_historyPath, json);
    }
    #endregion

    #region Customers (Clienti)

    private async Task<List<Customer>> LoadCustomersInternalAsync()
    {
        if (!File.Exists(_customersPath))
            return new List<Customer>();

        var json = await File.ReadAllTextAsync(_customersPath);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("clienti", out var clientiArray))
            return new List<Customer>();

        var customers = new List<Customer>();
        foreach (var c in clientiArray.EnumerateArray())
            customers.Add(JsonSerializer.Deserialize<Customer>(c.GetRawText(), JsonOptions)!);

        return customers;
    }
    
    public async Task<List<Customer>> LoadCustomersAsync()
    {
        await _customersSemaphore.WaitAsync();
        try
        {
            if (!File.Exists(_customersPath))
                return new List<Customer>();

            var json = await File.ReadAllTextAsync(_customersPath);
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
            // Backup
            if (File.Exists(_customersPath))
            {
                string backupPath = _customersPath + ".backup";
                File.Copy(_customersPath, backupPath, overwrite: true);
            }

            var wrapper = new { clienti = customers };
            var json = JsonSerializer.Serialize(wrapper, JsonOptions);
            await File.WriteAllTextAsync(_customersPath, json);
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
    
    private async Task SaveCustomersInternalAsync(IEnumerable<Customer> customers)
    {
        if (File.Exists(_customersPath))
            File.Copy(_customersPath, _customersPath + ".backup", overwrite: true);

        var wrapper = new { clienti = customers };
        var json = JsonSerializer.Serialize(wrapper, JsonOptions);
        await File.WriteAllTextAsync(_customersPath, json);
    }

    public async Task SaveOrUpdateCustomerAsync(Customer customer)
    {
        await _customersSemaphore.WaitAsync();
        try
        {
            // FIX: Usa il metodo INTERNAL che non acquisisce il semaforo
            var customers = await LoadCustomersInternalAsync();
            var existing = customers.FirstOrDefault(c =>
                c.BusinessName.Equals(customer.BusinessName, StringComparison.OrdinalIgnoreCase));

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
            customers.RemoveAll(c =>
                c.BusinessName.Equals(originalBusinessName, StringComparison.OrdinalIgnoreCase) ||
                c.BusinessName.Equals(customer.BusinessName, StringComparison.OrdinalIgnoreCase));

            customer.LastModifiedUtc = DateTime.UtcNow;
            customers.Add(customer);
            await SaveCustomersInternalAsync(customers);
        }
        finally
        {
            _customersSemaphore.Release();
        }
    }
    
    public async Task BulkUpdateCustomersAsync(IEnumerable<Customer> customersToAddOrUpdate)
    {
        await _customersSemaphore.WaitAsync();
        try
        {
            var existing = await LoadCustomersInternalAsync();
            var dict = existing
                .GroupBy(c => c.BusinessName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var c in customersToAddOrUpdate)
            {
                c.LastModifiedUtc = c.LastModifiedUtc == default ? DateTime.UtcNow : c.LastModifiedUtc;
                dict[c.BusinessName] = c;
            }

            await SaveCustomersInternalAsync(dict.Values);
            Debug.WriteLine($"[LocalJsonStore] BulkUpdateCustomers: {customersToAddOrUpdate.Count()} customers written");
        }
        finally
        {
            _customersSemaphore.Release();
        }
    }
    
    public async Task DeleteCustomerAsync(string businessName)
    {
        await _customersSemaphore.WaitAsync();
        try
        {
            var customers = await LoadCustomersInternalAsync();
            customers.RemoveAll(c =>
                c.BusinessName.Equals(businessName, StringComparison.OrdinalIgnoreCase));
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

    private static async Task WriteJsonWithBackupAsync<T>(string path, T value)
    {
        if (File.Exists(path))
            File.Copy(path, path + ".backup", overwrite: true);

        var json = JsonSerializer.Serialize(value, JsonOptions);
        await File.WriteAllTextAsync(path, json);
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
            IsJointVenture = entry.IsJointVenture,
            PartnerCompanyName = entry.PartnerCompanyName,
            OurCosts = entry.OurCosts,
            PartnerCosts = entry.PartnerCosts,
            AdditionalCosts = entry.AdditionalCosts,
            LastModifiedUtc = entry.LastModifiedUtc,
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

    #endregion
}

