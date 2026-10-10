using System.Diagnostics;
using System.IO;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

/// <summary>Prepares a current quote PDF without editing or saving the quote.</summary>
public sealed class QuotePdfDocumentService
{
    private static readonly object TemporaryPathGate = new();
    private readonly IDataService _data;
    private readonly Func<QuoteHistoryEntry, string> _expectedPath;
    private readonly AppSettingsServiceModel _appSettings;
    private readonly PdfTemplateSettingsModel _template;
    private readonly Func<string>? _selectedLogo;
    private readonly Func<IReadOnlyList<Customer>>? _customers;
    private readonly Action<PdfGenerationContext, Company, string> _writePdf;

    public QuotePdfDocumentService(
        IDataService data,
        StoragePathService storage,
        AppSettingsService settings,
        Func<string>? selectedLogo = null,
        Func<IReadOnlyList<Customer>>? customers = null)
        : this(data,
            entry => storage.BuildQuotePdfPath(entry.CustomerName, entry.QuoteNumber, entry.Date,
                string.IsNullOrWhiteSpace(entry.ReferenceName) ? null : entry.ReferenceName),
            settings.App, settings.PdfTemplate, selectedLogo, customers,
            (context, company, path) => new PdfService().GenerateQuoteFromContext(context, company, path))
    {
    }

    internal QuotePdfDocumentService(
        IDataService data,
        Func<QuoteHistoryEntry, string> expectedPath,
        AppSettingsServiceModel appSettings,
        PdfTemplateSettingsModel template,
        Func<string>? selectedLogo,
        Func<IReadOnlyList<Customer>>? customers,
        Action<PdfGenerationContext, Company, string> writePdf)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _expectedPath = expectedPath ?? throw new ArgumentNullException(nameof(expectedPath));
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _template = template ?? throw new ArgumentNullException(nameof(template));
        _selectedLogo = selectedLogo;
        _customers = customers;
        _writePdf = writePdf ?? throw new ArgumentNullException(nameof(writePdf));
    }

    public async Task<string> GetPdfPathAsync(string quoteNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quoteNumber);
        cancellationToken.ThrowIfCancellationRequested();
        var entry = await _data.GetQuoteByNumberAsync(quoteNumber.Trim(), cancellationToken, includeAttachments: true)
            ?? throw new InvalidOperationException("Preventivo non trovato nello storico.");
        cancellationToken.ThrowIfCancellationRequested();
        return await RegenerateAsync(entry, _expectedPath(entry), cancellationToken);
    }

    public async Task<string> RegenerateAsync(
        QuoteHistoryEntry fullEntry,
        string expectedPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fullEntry);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPath);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_appSettings.GeneratePDF)
            throw new InvalidOperationException(
                $"PDF del preventivo n. {fullEntry.QuoteNumber} non trovato e generazione PDF disabilitata nelle impostazioni.");

        var company = await _data.GetCompanyAsync() ?? new Company();
        cancellationToken.ThrowIfCancellationRequested();
        var customers = _customers != null
            ? _customers().ToList()
            : await _data.GetCustomersAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var context = CreateContext(fullEntry, company, customers, _selectedLogo?.Invoke(), _template);

        // Each request owns its temporary file, including concurrent requests for the same quote.
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            string temporaryRoot;
            // GetEffectiveTempPath checks writability with a fixed probe filename.
            lock (TemporaryPathGate) temporaryRoot = _appSettings.GetEffectiveTempPath();
            string temporaryPath = Path.Combine(temporaryRoot,
                $"{Path.GetFileNameWithoutExtension(expectedPath)}_{Guid.NewGuid():N}.pdf");
            try
            {
                _writePdf(context, company, temporaryPath);
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
                    throw new InvalidOperationException("Il PDF del preventivo non è stato generato.");

                try
                {
                    string? directory = Path.GetDirectoryName(expectedPath);
                    if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Copy(temporaryPath, expectedPath, overwrite: true);
                    TryDeleteTemporary(temporaryPath);
                    return expectedPath;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    Debug.WriteLine($"[QuotePdf] PDF rigenerato solo in temporaneo: {ex.Message}");
                    // An existing destination may contain an old PDF. Always return this new file.
                    return temporaryPath;
                }
            }
            catch
            {
                TryDeleteTemporary(temporaryPath);
                throw;
            }
        }, cancellationToken);
    }

    internal static PdfGenerationContext CreateContext(
        QuoteHistoryEntry entry,
        Company company,
        IReadOnlyList<Customer> customers,
        string? selectedLogo,
        PdfTemplateSettingsModel template)
    {
        string logo = selectedLogo ?? string.Empty;
        if (string.IsNullOrWhiteSpace(logo) && company.Logo.Count > 0)
        {
            int index = company.Logo_index >= 0 && company.Logo_index < company.Logo.Count ? company.Logo_index : 0;
            logo = company.Logo[index];
        }

        return new PdfGenerationContext
        {
            QuoteNumber = entry.QuoteNumber,
            Date = entry.Date,
            PaymentTerms = entry.PaymentTerms,
            CustomerNotes = entry.CustomerNotes,
            IvaType = entry.IvaType,
            CustomerName = entry.CustomerName,
            ReferenceName = entry.ReferenceName,
            SiteName = entry.SiteName,
            BillingCustomerName = entry.BillingCustomerName,
            SelectedLogo = logo,
            MaterialDiscount = entry.MaterialDiscount,
            LaborDiscount = entry.LaborDiscount,
            Materials = entry.Materials.Select(CloneItem).ToList(),
            Labors = entry.Labors.Select(CloneItem).ToList(),
            Imponibile = entry.Imponibile,
            Total = entry.Total,
            Attachments = entry.Attachments.Where(attachment => attachment.Content.Length > 0)
                .Select(attachment => new StoredFile
                {
                    FileName = attachment.FileName,
                    ContentType = attachment.ContentType,
                    Content = attachment.Content.ToArray(),
                    ImportedAt = attachment.ImportedAt
                }).ToList(),
            AllCustomers = customers.Select(customer => new Customer
            {
                SyncId = customer.SyncId,
                BusinessName = customer.BusinessName,
                Address = customer.Address,
                Email = customer.Email,
                Phone = customer.Phone,
                PreferredVatType = customer.PreferredVatType,
                MaterialDiscount = customer.MaterialDiscount,
                LaborDiscount = customer.LaborDiscount,
                SupplierDiscount = customer.SupplierDiscount,
                IsSupplier = customer.IsSupplier,
                LastModifiedUtc = customer.LastModifiedUtc
            }).ToList(),
            PdfTemplateName = template.ActiveTemplate,
            PdfNotesTitle = template.NotesTitle,
            PdfFooterText = template.FooterText,
            PdfSignatureText = template.SignatureText,
            PdfShowTemplateName = template.ShowTemplateName
        };
    }

    private static Item CloneItem(Item item) => new()
    {
        PersistentId = item.PersistentId,
        Name = item.Name,
        Description = item.Description,
        UnitPrice = item.UnitPrice,
        Quantity = item.Quantity,
        UnitOfMeasure = item.UnitOfMeasure,
        Discount = item.Discount,
        IsSignificant = item.IsSignificant,
        IsCompanyMaterial = item.IsCompanyMaterial,
        ExcludeFromWorkSheet = item.ExcludeFromWorkSheet,
        SortOrder = item.SortOrder
    };

    private static void TryDeleteTemporary(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Debug.WriteLine($"[QuotePdf] Pulizia PDF temporaneo non riuscita: {ex.Message}"); }
    }
}
