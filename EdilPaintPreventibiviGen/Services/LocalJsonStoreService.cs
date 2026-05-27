using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly SemaphoreSlim _historySemaphore = new(1, 1);
    private readonly SemaphoreSlim _customersSemaphore = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public LocalJsonStoreService(string assetsPath)
    {
        _historyPath = Path.Combine(assetsPath, "history.json");
        _customersPath = Path.Combine(assetsPath, "clienti.json");

        Directory.CreateDirectory(assetsPath);
    }

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
                // NON aggiornare LastModifiedUtc se l'entry viene dal DB — preserva il timestamp originale
                if (entry.LastModifiedUtc == default)
                    entry.LastModifiedUtc = DateTime.UtcNow;

                // Ricalcola l'hash solo se non è già presente (il DB lo porta già aggiornato)
                if (string.IsNullOrEmpty(entry.SyncHash))
                    entry.SyncHash = ComputeQuoteHash(entry);

                historyDict[entry.QuoteNumber] = entry;
            }

            await SaveHistoryInternalAsync(historyDict.Values);
            Debug.WriteLine($"[LocalJsonStore] BulkUpdate: {entriesToAddOrUpdate.Count()} quotes written");
        }
        finally
        {
            _historySemaphore.Release();
        }
    }
    
    public async Task SaveHistoryAsync(IEnumerable<QuoteHistoryEntry> entries)
    {
        await _historySemaphore.WaitAsync();
        try
        {
            // Backup prima di sovrascrivere
            if (File.Exists(_historyPath))
            {
                string backupPath = _historyPath + ".backup";
                File.Copy(_historyPath, backupPath, overwrite: true);
            }

            var json = JsonSerializer.Serialize(entries, JsonOptions);
            await File.WriteAllTextAsync(_historyPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalJsonStore] Error saving history: {ex.Message}");
            throw;
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
            var existing = history.FirstOrDefault(q =>
                q.QuoteNumber.Equals(entry.QuoteNumber, StringComparison.OrdinalIgnoreCase));

            entry.LastModifiedUtc = DateTime.UtcNow;
            entry.SyncHash = ComputeQuoteHash(entry);

            if (existing != null)
                history.Remove(existing);

            history.Add(entry);
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

        var json = JsonSerializer.Serialize(entries, JsonOptions);
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

    private static string ComputeQuoteHash(QuoteHistoryEntry entry)
    {
        var materialsHash = string.Join("|", entry.Materials
            .OrderBy(m => m.Name)
            .Select(m => $"{m.Name}:{m.UnitPrice}:{m.Quantity}:{m.Discount}"));
    
        var laborsHash = string.Join("|", entry.Labors
            .OrderBy(l => l.Name)
            .Select(l => $"{l.Name}:{l.UnitPrice}:{l.Quantity}:{l.Discount}"));

        var costsHash = $"{entry.IsJointVenture}|{entry.PartnerCompanyName}|" +
            string.Join("|", entry.OurCosts.Select(c => $"{c.Description}:{c.Amount}")) + "|" +
            string.Join("|", entry.PartnerCosts.Select(c => $"{c.Description}:{c.Amount}")) + "|" +
            string.Join("|", entry.AdditionalCosts.Select(c => $"{c.Description}:{c.Amount}"));

        // AGGIUNTO: include la dimensione del PDF nell'hash.
        // Se il PDF cambia, l'hash cambia, e il sync lo rileva.
        var pdfHash = entry.PdfFile != null
            ? (entry.PdfFile.Content.Length > 0
                ? entry.PdfFile.Content.Length.ToString()
                : "has-pdf")   // ← ha il PDF ma senza bytes (lightEntry)
            : "no-pdf";

        var data = $"{entry.QuoteNumber}|{entry.Date:O}|{entry.CustomerName}|" +
            $"{entry.Total}|{entry.Status}|{entry.IvaType}|" +
            $"{entry.MaterialDiscount}|{entry.LaborDiscount}|" +
            $"{materialsHash}|{laborsHash}|{costsHash}|{pdfHash}"; // ← aggiunto |{pdfHash}

        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(data);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    #endregion
}