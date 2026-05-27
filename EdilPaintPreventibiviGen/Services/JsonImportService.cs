using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public class JsonImportService
{
    private readonly IDataService _dataService;
    private readonly StoragePathService _storagePathService = StoragePathService.Instance;

    public JsonImportService(IDataService dataService)
    {
        _dataService = dataService;
    }

    public async Task ImportAllAsync(string assetsPath)
    {
        await ImportCompanyAsync(Path.Combine(assetsPath, "azienda.json"));
        await ImportCustomersAsync(Path.Combine(assetsPath, "clienti.json"));
        await ImportLaborsAsync(Path.Combine(assetsPath, "dati_lavori.json"));
        await ImportPersonalMaterialsAsync(Path.Combine(assetsPath, "materiali_personali.json"));
        await ImportHistoryAsync(Path.Combine(assetsPath, "history.json"));
    }

    public async Task<bool> IsDatabaseEmptyAsync()
    {
        return await _dataService.IsDatabaseEmptyAsync();
    }

    private async Task ImportCompanyAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return;

        var json = await File.ReadAllTextAsync(filePath);
        var company = JsonSerializer.Deserialize<Company>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (company == null)
            return;

        string selectedLogo = string.Empty;
        if (company.Logo_index >= 0 && company.Logo_index < company.Logo.Count)
        {
            selectedLogo = Path.GetFileName(company.Logo[company.Logo_index]);
        }

        Debug.WriteLine($"[IMPORT COMPANY] Logo ricevuti: {string.Join(" | ", company.Logo)}");
        Debug.WriteLine($"[IMPORT COMPANY] selectedLogo: {selectedLogo}");
        await _dataService.SaveCompanyAsync(company, selectedLogo);
    }

    private async Task ImportCustomersAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return;

        var json = await File.ReadAllTextAsync(filePath);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("clienti", out var clientiArray))
            return;

        var existingCustomers = await _dataService.GetCustomersAsync();
        var existingNames = new HashSet<string>(
            existingCustomers.Select(c => c.BusinessName.Trim()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var c in clientiArray.EnumerateArray())
        {
            var customer = new Customer
            {
                BusinessName = GetJsonString(c, "Ragione Sociale", "ragione sociale", "BusinessName"),
                Address = GetJsonString(c, "Indirizzo", "indirizzo", "Address"),
                Email = GetJsonString(c, "email", "Email", "EMAIL"),
                Phone = GetJsonString(c, "Telefono", "telefono", "Phone", "tel"),
                MaterialDiscount = GetJsonDouble(c, "sconto_materiale"),
                LaborDiscount = GetJsonDouble(c, "sconto_lavori")
            };

            if (string.IsNullOrWhiteSpace(customer.BusinessName))
                continue;

            if (existingNames.Contains(customer.BusinessName))
                continue;

            await _dataService.AddCustomerAsync(customer);
            existingNames.Add(customer.BusinessName);
        }
    }

    private async Task ImportLaborsAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return;

        var json = await File.ReadAllTextAsync(filePath);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("lavori", out var lavoriArray))
            return;

        var labors = new List<Item>();

        foreach (var e in lavoriArray.EnumerateArray())
        {
            labors.Add(new Item
            {
                Name = GetJsonString(e, "nome", "Nome", "name"),
                Description = GetJsonString(e, "descrizione", "Descrizione", "description"),
                UnitPrice = GetJsonDouble(e, "valore", "Valore", "unitPrice"),
                Quantity = 1
            });
        }

        await _dataService.SaveLaborCatalogAsync(labors);
    }

    private async Task ImportPersonalMaterialsAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return;

        var json = await File.ReadAllTextAsync(filePath);
        var materials = JsonSerializer.Deserialize<List<Item>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (materials == null)
            return;

        await _dataService.SavePersonalMaterialsAsync(materials);
    }

    private async Task ImportHistoryAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return;

        var json = await File.ReadAllTextAsync(filePath);
        var history = JsonSerializer.Deserialize<List<QuoteHistoryEntry>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (history == null)
            return;

        var existingQuotes = await _dataService.GetQuotesAsync();
        var existingNumbers = new HashSet<string>(
            existingQuotes.Select(q => q.QuoteNumber.Trim()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in history)
        {
            if (string.IsNullOrWhiteSpace(entry.QuoteNumber))
                continue;

            if (existingNumbers.Contains(entry.QuoteNumber))
                continue;

            entry.PdfFile = LoadLegacyPdf(entry);
            entry.Attachments = LoadLegacyAttachments(entry);

            await _dataService.SaveQuoteAsync(entry);
            existingNumbers.Add(entry.QuoteNumber);
        }
    }
    
    private StoredFile? LoadLegacyPdf(QuoteHistoryEntry entry)
    {
        try
        {
            var candidateFiles = BuildPdfCandidatePaths(entry);

            string? existingPath = candidateFiles
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));

            if (string.IsNullOrWhiteSpace(existingPath))
                return null;

            return new StoredFile
            {
                FileName = Path.GetFileName(existingPath),
                ContentType = "application/pdf",
                Content = File.ReadAllBytes(existingPath),
                ImportedAt = File.GetLastWriteTime(existingPath)
            };
        }
        catch
        {
            return null;
        }
    }

    private List<string> BuildPdfCandidatePaths(QuoteHistoryEntry entry)
    {
        var result = new List<string>();

        if (!string.IsNullOrWhiteSpace(entry.PdfPath))
        {
            result.Add(entry.PdfPath);
        }

        string? fileNameFromPath = !string.IsNullOrWhiteSpace(entry.PdfPath)
            ? Path.GetFileName(entry.PdfPath)
            : null;

        string fallbackFileName = !string.IsNullOrWhiteSpace(fileNameFromPath)
            ? fileNameFromPath
            : $"Preventivo_{entry.QuoteNumber}.pdf";

        foreach (var customerVariant in BuildFolderNameVariants(entry.CustomerName))
        {
            if (string.IsNullOrWhiteSpace(entry.ReferenceName))
            {
                TryAddCustomerFolderCandidates(result, customerVariant, null, fallbackFileName, entry.QuoteNumber);
            }
            else
            {
                foreach (var referenceVariant in BuildFolderNameVariants(entry.ReferenceName))
                {
                    TryAddCustomerFolderCandidates(result, customerVariant, referenceVariant, fallbackFileName, entry.QuoteNumber);
                }
            }
        }

        return result
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
    
    private List<string> BuildFolderNameVariants(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ["N_A"];

        string trimmed = value.Trim();
        string underscore = trimmed.Replace(" ", "_");
        string spaced = trimmed.Replace("_", " ");
        string sanitizedOriginal = StoragePathService.SanitizeFolderName(trimmed);
        string sanitizedUnderscore = StoragePathService.SanitizeFolderName(underscore);
        string sanitizedSpaced = StoragePathService.SanitizeFolderName(spaced);

        return new[]
            {
                trimmed,
                underscore,
                spaced,
                sanitizedOriginal,
                sanitizedUnderscore,
                sanitizedSpaced
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
    
    private void TryAddCustomerFolderCandidates(List<string> result, string customerNameVariant, string? referenceNameVariant, string fallbackFileName, string quoteNumber)
    {
        try
        {
            string folder = _storagePathService.BuildCustomerPdfFolder(customerNameVariant, referenceNameVariant);

            result.Add(Path.Combine(folder, fallbackFileName));

            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.GetFiles(folder, $"*Preventivo*{quoteNumber}*.pdf", SearchOption.TopDirectoryOnly))
                {
                    result.Add(file);
                }
            }
        }
        catch
        {
        }
    }
    

    private List<StoredFile> LoadLegacyAttachments(QuoteHistoryEntry entry)
    {
        var result = new List<StoredFile>();

        try
        {
            if (string.IsNullOrWhiteSpace(entry.CustomerName) || string.IsNullOrWhiteSpace(entry.QuoteNumber))
                return result;

            string folder = _storagePathService.BuildCustomerPdfFolder(entry.CustomerName,
                string.IsNullOrWhiteSpace(entry.ReferenceName) ? null : entry.ReferenceName);

            if (!Directory.Exists(folder))
                return result;

            string attachmentsFolder = Path.Combine(folder, $"Allegati_{entry.QuoteNumber}");
            if (!Directory.Exists(attachmentsFolder))
                return result;

            foreach (var file in Directory.GetFiles(attachmentsFolder))
            {
                try
                {
                    result.Add(new StoredFile
                    {
                        FileName = Path.GetFileName(file),
                        ContentType = GetContentType(file),
                        Content = File.ReadAllBytes(file),
                        ImportedAt = File.GetLastWriteTime(file)
                    });
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return result;
    }

    private static string GetContentType(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".pdf" => "application/pdf",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
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
                double.TryParse(prop.GetString(), out value))
                return value;
        }

        return 0;
    }
}