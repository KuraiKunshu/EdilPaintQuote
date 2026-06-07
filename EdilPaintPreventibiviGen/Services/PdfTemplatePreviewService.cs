using System.Diagnostics;
using System.IO;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed class PdfTemplatePreviewService
{
    private readonly IDataService _dataService;
    private readonly PdfService _pdfService = new();

    public PdfTemplatePreviewService(IDataService dataService)
    {
        _dataService = dataService;
    }

    public async Task<string> GenerateQuotePreviewAsync(
        PdfTemplateSettingsModel template,
        CancellationToken cancellationToken = default)
    {
        template.Normalize();

        var company = await LoadCompanyAsync().ConfigureAwait(false);
        var context = CreateDemoContext(template, company);

        string tempRoot = App.AppSettings.App.GetEffectiveTempPath();
        Directory.CreateDirectory(tempRoot);
        string safeTemplateName = string.Concat(template.ActiveTemplate.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string previewPath = Path.Combine(
            tempRoot,
            $"anteprima_template_{safeTemplateName}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(() => _pdfService.GenerateQuoteFromContext(context, company, previewPath), cancellationToken)
            .ConfigureAwait(false);

        return previewPath;
    }

    public static void OpenPreview(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private async Task<Company> LoadCompanyAsync()
    {
        try
        {
            return await _dataService.GetCompanyAsync().ConfigureAwait(false) ?? CreateFallbackCompany();
        }
        catch
        {
            return CreateFallbackCompany();
        }
    }

    private static PdfGenerationContext CreateDemoContext(PdfTemplateSettingsModel template, Company company)
    {
        var customer = new Customer
        {
            BusinessName = "Cliente Demo S.r.l.",
            Address = "Via Roma 12, 37100 Verona",
            Email = "cliente.demo@example.com",
            Phone = "045 000000"
        };

        var reference = new Customer
        {
            BusinessName = "Arch. Rossi",
            Address = "Studio tecnico - Verona",
            Email = "studio.rossi@example.com",
            Phone = "045 111111"
        };

        var materials = new List<Item>
        {
            new()
            {
                Name = "Pittura lavabile premium",
                Description = "Fornitura materiale per finitura interna",
                UnitPrice = 18.50,
                Quantity = 12,
                Discount = 5,
                SortOrder = 0
            },
            new()
            {
                Name = "Rasante fibrorinforzato",
                Description = "Materiale significativo per preparazione supporto",
                UnitPrice = 32,
                Quantity = 6,
                IsSignificant = true,
                SortOrder = 1
            }
        };

        var labors = new List<Item>
        {
            new()
            {
                Name = "Preparazione superfici",
                Description = "Carteggiatura, stuccatura e protezione aree",
                UnitPrice = 280,
                Quantity = 1,
                SortOrder = 0
            },
            new()
            {
                Name = "Applicazione finitura",
                Description = "Due mani a rullo con riprese manuali",
                UnitPrice = 420,
                Quantity = 1,
                Discount = 3,
                SortOrder = 1
            }
        };

        var calculator = new QuoteCalculator();
        var totals = calculator.Calculate(materials, labors, 7, 4, "RC 10%+22%");

        return new PdfGenerationContext
        {
            QuoteNumber = "PREVIEW-001",
            Date = DateTime.Today,
            PaymentTerms = "Pagamento 30% all'accettazione, saldo a fine lavori.",
            IvaType = "RC 10%+22%",
            CustomerName = customer.BusinessName,
            ReferenceName = reference.BusinessName,
            SelectedLogo = ResolveSelectedLogo(company),
            MaterialDiscount = 7,
            LaborDiscount = 4,
            Materials = materials,
            Labors = labors,
            Imponibile = totals.Imponibile,
            Total = totals.TotaleGenerale,
            AllCustomers = [customer, reference],
            PdfTemplateName = template.ActiveTemplate,
            PdfNotesTitle = template.NotesTitle,
            PdfFooterText = template.FooterText,
            PdfSignatureText = template.SignatureText,
            PdfShowTemplateName = true
        };
    }

    private static Company CreateFallbackCompany()
    {
        return new Company
        {
            Nome = "EdilPaint",
            Indirizzo = "Sede demo",
            Piva = "00000000000",
            Email = "info@example.com",
            Logo = ["Edilpaint.png"],
            Logo_index = 0,
            Termini_pagamento = "Pagamento da concordare."
        };
    }

    private static string ResolveSelectedLogo(Company company)
    {
        if (company.Logo.Count == 0)
            return "Edilpaint.png";

        int index = Math.Clamp(company.Logo_index, 0, company.Logo.Count - 1);
        string logo = company.Logo[index];
        return string.IsNullOrWhiteSpace(logo) ? "Edilpaint.png" : Path.GetFileName(logo);
    }
}
