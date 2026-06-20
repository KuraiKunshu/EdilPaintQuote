using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using EdilPaintPreventibiviGen;
using EdilPaintPreventibiviGen.Helpers;
using EdilPaintPreventibiviGen.ViewModels;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public class PdfService
{
    private static class PdfPalette
    {
        public static readonly Color White = ThemeResources.GetPdfColor("PdfWhiteColor");
        public static readonly Color AccentRed = ThemeResources.GetPdfColor("PdfAccentRedColor");
        public static readonly Color RedDarken2 = ThemeResources.GetPdfColor("PdfRedDarken2Color");
        public static readonly Color OrangeMedium = ThemeResources.GetPdfColor("PdfOrangeMediumColor");
        public static readonly Color BlueDarken1 = ThemeResources.GetPdfColor("PdfBlueDarken1Color");
        public static readonly Color GreenMedium = ThemeResources.GetPdfColor("PdfGreenMediumColor");
        public static readonly Color GreenDarken1 = ThemeResources.GetPdfColor("PdfGreenDarken1Color");
        public static readonly Color GreenDarken2 = ThemeResources.GetPdfColor("PdfGreenDarken2Color");
        public static readonly Color GreyLighten1 = ThemeResources.GetPdfColor("PdfGreyLighten1Color");
        public static readonly Color GreyLighten2 = ThemeResources.GetPdfColor("PdfGreyLighten2Color");
        public static readonly Color GreyLighten3 = ThemeResources.GetPdfColor("PdfGreyLighten3Color");
        public static readonly Color GreyMedium = ThemeResources.GetPdfColor("PdfGreyMediumColor");
        public static readonly Color GreyDarken1 = ThemeResources.GetPdfColor("PdfGreyDarken1Color");
        public static readonly Color GreyDarken2 = ThemeResources.GetPdfColor("PdfGreyDarken2Color");
        public static readonly Color GreyDarken3 = ThemeResources.GetPdfColor("PdfGreyDarken3Color");
    }

    private sealed class PdfTemplateStyle
    {
        public float MarginCm { get; init; } = 1.5f;
        public float BodyFontSize { get; init; } = 10;
        public float TitleFontSize { get; init; } = 22;
        public Color AccentColor { get; init; } = PdfPalette.AccentRed;
        public Color TitleColor { get; init; } = PdfPalette.GreyLighten2;

        public static PdfTemplateStyle Resolve(string? templateName)
        {
            return templateName?.Trim() switch
            {
                "Compatto" => new PdfTemplateStyle
                {
                    MarginCm = 1.0f,
                    BodyFontSize = 9,
                    TitleFontSize = 20,
                    AccentColor = PdfPalette.RedDarken2
                },
                "Collaborazione" => new PdfTemplateStyle
                {
                    MarginCm = 1.35f,
                    BodyFontSize = 9.5f,
                    TitleFontSize = 21,
                    AccentColor = PdfPalette.BlueDarken1,
                    TitleColor = PdfPalette.BlueDarken1
                },
                "Cliente privato" => new PdfTemplateStyle
                {
                    MarginCm = 1.6f,
                    BodyFontSize = 10.5f,
                    TitleFontSize = 22,
                    AccentColor = PdfPalette.GreenDarken1,
                    TitleColor = PdfPalette.GreenDarken1
                },
                "Impresa" => new PdfTemplateStyle
                {
                    MarginCm = 1.35f,
                    BodyFontSize = 10,
                    TitleFontSize = 23,
                    AccentColor = PdfPalette.GreyDarken3,
                    TitleColor = PdfPalette.GreyDarken3
                },
                _ => new PdfTemplateStyle()
            };
        }
    }

    public static double CalculateEstimatedMargin(CostsPdfContext ctx)
    {
        double totalCosts =
            ctx.OurCosts.Sum(c => c.Amount) +
            ctx.PartnerCosts.Sum(c => c.Amount) +
            ctx.AdditionalCosts.Sum(c => c.Amount);

        return ctx.Imponibile - totalCosts;
    }

    /// <summary>
    /// Punto di ingresso per la generazione da UI (MainViewModel).
    /// Converte il ViewModel in PdfGenerationContext e delega.
    /// </summary>
    public void GenerateQuote(MainViewModel vm, Company company, string filePath, DateTime? quoteDate = null)
    {
        var ctx = new PdfGenerationContext
        {
            QuoteNumber = vm.QuoteNumber,
            Date = quoteDate ?? DateTime.Now,
            PaymentTerms = vm.PaymentTerms,
            IvaType = vm.IvaType,
            CustomerName = vm.SelectedCustomer?.BusinessName ?? string.Empty,
            ReferenceName = vm.IsSecondCustomerEnabled ? (vm.SelectedSecondCustomer?.BusinessName ?? string.Empty) : string.Empty,
            SelectedLogo = vm.SelectedLogo,
            MaterialDiscount = vm.MaterialDiscount,
            LaborDiscount = vm.LaborDiscount,
            Materials = vm.Materials.ToList(),
            Labors = vm.Labors.ToList(),
            Imponibile = vm.Imponibile,
            Total = vm.TotaleGenerale,
            Attachments = vm.AttachedImages.Select(a => new StoredFile
            {
                FileName = a.FileName,
                ContentType = a.ContentType,
                Content = a.Content,
                ImportedAt = DateTime.Now
            }).ToList(),
            AllCustomers = vm.AllCustomers.ToList(),
            PdfTemplateName = App.AppSettings.PdfTemplate.ActiveTemplate,
            PdfNotesTitle = App.AppSettings.PdfTemplate.NotesTitle,
            PdfFooterText = App.AppSettings.PdfTemplate.FooterText,
            PdfSignatureText = App.AppSettings.PdfTemplate.SignatureText,
            PdfShowTemplateName = App.AppSettings.PdfTemplate.ShowTemplateName
        };

        GenerateQuoteFromContext(ctx, company, filePath);
    }

    /// <summary>
    /// Metodo principale di generazione PDF — non dipende dal ViewModel.
    /// </summary>
    public void GenerateQuoteFromContext(PdfGenerationContext ctx, Company company, string filePath)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        NormalizePdfTemplate(ctx);
        var templateStyle = PdfTemplateStyle.Resolve(ctx.PdfTemplateName);

        var customer = ctx.AllCustomers.FirstOrDefault(c => c.BusinessName == ctx.CustomerName);
        var reference = !string.IsNullOrWhiteSpace(ctx.ReferenceName)
            ? ctx.AllCustomers.FirstOrDefault(c => c.BusinessName == ctx.ReferenceName)
            : null;

        var calculator = new QuoteCalculator();
        var totals = calculator.Calculate(ctx.Materials, ctx.Labors, ctx.MaterialDiscount, ctx.LaborDiscount, ctx.IvaType);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(templateStyle.MarginCm, Unit.Centimetre);
                page.PageColor(PdfPalette.White);
                page.DefaultTextStyle(x => x.FontSize(templateStyle.BodyFontSize).FontFamily("Segoe UI"));

                #region Header
                page.Header().ShowOnce().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                        string assetsPath = Path.Combine(baseDir, "Assets");
                        if (!Directory.Exists(assetsPath)) assetsPath = Path.Combine(baseDir, "assets");
                        if (!Directory.Exists(assetsPath)) assetsPath = Path.Combine(baseDir, "..", "..", "..", "Assets");

                        string logoPath = Path.Combine(assetsPath, ctx.SelectedLogo);
                        if (!string.IsNullOrEmpty(ctx.SelectedLogo) && File.Exists(logoPath))
                            col.Item().Height(60).Image(Image.FromFile(logoPath)).FitHeight();
                        else
                            col.Item().Height(60);

                        col.Item().PaddingTop(10).Text(company.Nome).FontSize(14).Bold().FontColor(templateStyle.AccentColor);
                        col.Item().Text(company.Indirizzo).FontSize(9).FontColor(PdfPalette.GreyDarken2);
                        col.Item().Text($"P.IVA: {company.Piva}").FontSize(9).FontColor(PdfPalette.GreyDarken2);
                        col.Item().Text($"Email: {company.Email}").FontSize(9).FontColor(PdfPalette.GreyDarken2);
                    });

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().AlignRight().Text("PREVENTIVO")
                            .FontSize(templateStyle.TitleFontSize + 2).ExtraBold().FontColor(PdfPalette.GreyDarken3);
                        col.Item().PaddingTop(3).AlignRight().Width(220)
                            .LineHorizontal(1.2f).LineColor(templateStyle.AccentColor);
                        col.Item().PaddingTop(7).AlignRight().Text($"N. {ctx.QuoteNumber}")
                            .FontSize(16).ExtraBold().FontColor(templateStyle.AccentColor);
                        if (ctx.PdfShowTemplateName)
                            col.Item().AlignRight().Text($"Template: {ctx.PdfTemplateName}").FontSize(8).FontColor(PdfPalette.GreyMedium);

                        var dataScadenza = ctx.Date.AddMonths(1).AddDays(15);
                        col.Item().PaddingTop(5).AlignRight().Text($"Data: {ctx.Date:dd/MM/yyyy}")
                            .FontSize(9).FontColor(PdfPalette.GreyDarken2);
                        col.Item().AlignRight().Text($"Validita': {dataScadenza:dd/MM/yyyy}")
                            .FontSize(9).FontColor(PdfPalette.GreyDarken2);
                    });
                });
                #endregion

                page.Content().PaddingVertical(25).Column(col =>
                {
                    if (customer != null)
                    {
                        col.Item().PaddingBottom(22).BorderLeft(4).BorderColor(templateStyle.AccentColor)
                            .Background(PdfPalette.GreyLighten3).Padding(13).Row(clientRow =>
                            {
                                clientRow.RelativeItem().Column(custCol =>
                                {
                                    custCol.Item().Text("SPETTABILE")
                                        .FontSize(8).SemiBold().FontColor(PdfPalette.GreyDarken1);
                                    custCol.Item().PaddingTop(2).Text(customer.BusinessName)
                                        .Bold().FontSize(12).FontColor(PdfPalette.GreyDarken3);
                                    if (!string.IsNullOrWhiteSpace(customer.Address))
                                        custCol.Item().Text(customer.Address).FontSize(9);
                                    if (!string.IsNullOrWhiteSpace(customer.Phone))
                                        custCol.Item().Text($"Tel: {customer.Phone}").FontSize(8).FontColor(PdfPalette.GreyDarken2);
                                    if (!string.IsNullOrWhiteSpace(customer.Email))
                                        custCol.Item().Text($"Mail: {customer.Email}").FontSize(8).FontColor(PdfPalette.GreyDarken2);
                                });

                                if (reference != null)
                                {
                                    clientRow.ConstantItem(18);
                                    clientRow.RelativeItem().BorderLeft(1).BorderColor(PdfPalette.GreyLighten1)
                                        .PaddingLeft(14).Column(refCol =>
                                        {
                                            refCol.Item().Text("RIFERIMENTO")
                                                .FontSize(8).SemiBold().FontColor(PdfPalette.GreyDarken1);
                                            refCol.Item().PaddingTop(2).Text(reference.BusinessName)
                                                .Bold().FontSize(11).FontColor(templateStyle.AccentColor);
                                            if (!string.IsNullOrWhiteSpace(reference.Address))
                                                refCol.Item().Text(reference.Address).FontSize(9);
                                            if (!string.IsNullOrWhiteSpace(reference.Phone))
                                                refCol.Item().Text($"Tel: {reference.Phone}").FontSize(8).FontColor(PdfPalette.GreyDarken2);
                                            if (!string.IsNullOrWhiteSpace(reference.Email))
                                                refCol.Item().Text($"Mail: {reference.Email}").FontSize(8).FontColor(PdfPalette.GreyDarken2);
                                        });
                                }
                            });
                    }

                    #region Materiali
                    if (ctx.Materials.Count > 0)
                    {
                        col.Item().BorderBottom(1).BorderColor(templateStyle.AccentColor).PaddingBottom(5)
                           .Text("MATERIALI").FontSize(11).Bold().FontColor(templateStyle.AccentColor);

                        col.Item().PaddingBottom(15).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3); columns.ConstantColumn(40);
                                columns.ConstantColumn(80); columns.ConstantColumn(80);
                            });
                            table.Header(header =>
                            {
                                header.Cell().Element(TableHeaderStyle).Text("Descrizione").SemiBold();
                                header.Cell().Element(TableHeaderStyle).AlignCenter().Text("Q.ta'").SemiBold();
                                header.Cell().Element(TableHeaderStyle).AlignRight().Text("Prezzo unit.").SemiBold();
                                header.Cell().Element(TableHeaderStyle).AlignRight().Text("Totale").SemiBold();

                                static IContainer TableHeaderStyle(IContainer c) =>
                                    c.Background(PdfPalette.GreyLighten3)
                                        .PaddingVertical(6)
                                        .PaddingHorizontal(5);
                            });
                            foreach (var item in ctx.Materials)
                            {
                                table.Cell().Element(RowStyle).Column(c =>
                                {
                                    c.Item().Text(text =>
                                    {
                                        text.Span(item.Name).Bold();
                                        if (item.IsSignificant)
                                            text.Span(" [*]").FontSize(8).FontColor(templateStyle.AccentColor).Italic();
                                    });
                                    if (!string.IsNullOrWhiteSpace(item.Description))
                                        c.Item().Text(item.Description).FontSize(8).FontColor(PdfPalette.GreyDarken1);
                                });
                                table.Cell().Element(RowStyle).AlignCenter().Text(item.Quantity.ToString());
                                table.Cell().Element(RowStyle).AlignRight().Text(text =>
                                {
                                    text.Line($"{item.UnitPrice:N2} €");
                                    double totalDiscount = item.Discount + ctx.MaterialDiscount;
                                    if (totalDiscount > 0)
                                        text.Line($"SCONTO -{totalDiscount:0.##}%")
                                            .FontSize(9)
                                            .Bold()
                                            .FontColor(PdfPalette.RedDarken2);
                                });
                                table.Cell().Element(RowStyle).AlignRight().Text($"{item.TotalPrice * (1 - ctx.MaterialDiscount / 100):N2} €");
                                static IContainer RowStyle(IContainer c) => c.BorderBottom(0.5f).BorderColor(PdfPalette.GreyLighten3).PaddingVertical(5);
                            }
                        });
                    }
                    #endregion

                    #region Lavorazioni
                    if (ctx.Labors.Count > 0)
                    {
                        col.Item().BorderBottom(1).BorderColor(templateStyle.AccentColor).PaddingBottom(5)
                           .Text("VOCI MANODOPERA").FontSize(11).Bold().FontColor(templateStyle.AccentColor);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3); columns.ConstantColumn(40);
                                columns.ConstantColumn(80); columns.ConstantColumn(80);
                            });
                            table.Header(header =>
                            {
                                header.Cell().Element(TableHeaderStyle).Text("Descrizione").SemiBold();
                                header.Cell().Element(TableHeaderStyle).AlignCenter().Text("Q.ta'").SemiBold();
                                header.Cell().Element(TableHeaderStyle).AlignRight().Text("Prezzo unit.").SemiBold();
                                header.Cell().Element(TableHeaderStyle).AlignRight().Text("Totale").SemiBold();

                                static IContainer TableHeaderStyle(IContainer c) =>
                                    c.Background(PdfPalette.GreyLighten3)
                                        .PaddingVertical(6)
                                        .PaddingHorizontal(5);
                            });
                            foreach (var item in ctx.Labors)
                            {
                                table.Cell().Element(RowStyle).Column(c =>
                                {
                                    c.Item().Text(text =>
                                    {
                                        text.Span(item.Name).Bold();
                                        if (item.IsSignificant)
                                            text.Span(" [*]").FontSize(8).FontColor(templateStyle.AccentColor).Italic();
                                    });
                                    if (!string.IsNullOrWhiteSpace(item.Description))
                                        c.Item().Text(item.Description).FontSize(8).FontColor(PdfPalette.GreyDarken1);
                                });
                                table.Cell().Element(RowStyle).AlignCenter().Text(item.Quantity.ToString());
                                table.Cell().Element(RowStyle).AlignRight().Text(text =>
                                {
                                    double totalDiscount = item.Discount + ctx.LaborDiscount;
                                    text.Line($"{item.UnitPrice:N2} €");
                                    if (totalDiscount > 0)
                                        text.Line($"SCONTO -{totalDiscount:0.##}%")
                                            .FontSize(9)
                                            .Bold()
                                            .FontColor(PdfPalette.RedDarken2);
                                });
                                table.Cell().Element(RowStyle).AlignRight().Text($"{item.TotalPrice * (1 - ctx.LaborDiscount / 100):N2} €");
                                static IContainer RowStyle(IContainer c) => c.BorderBottom(0.5f).BorderColor(PdfPalette.GreyLighten3).PaddingVertical(5);
                            }
                        });
                    }
                    #endregion

                    col.Item().PaddingTop(30).BorderTop(1).BorderColor(PdfPalette.GreyLighten1).Row(row =>
                    {
                        #region Note
                        row.RelativeItem().PaddingRight(30).Column(noteCol =>
                        {
                            noteCol.Item().Text(ctx.PdfNotesTitle).FontSize(9).Bold().FontColor(templateStyle.AccentColor);
                            noteCol.Item().PaddingTop(5).Text(ctx.PaymentTerms).FontSize(9).LineHeight(1.2f);
                            noteCol.Item().PaddingTop(18).Text(ctx.PdfSignatureText)
                                .FontSize(9).SemiBold().FontColor(PdfPalette.GreyDarken2);
                            noteCol.Item().PaddingTop(8).Row(signatureRow =>
                            {
                                signatureRow.RelativeItem().BorderBottom(1)
                                    .BorderColor(PdfPalette.GreyDarken1).Height(14);
                                signatureRow.ConstantItem(18);
                                signatureRow.RelativeItem().BorderBottom(1)
                                    .BorderColor(PdfPalette.GreyDarken1).Height(14);
                            });
                            noteCol.Item().PaddingTop(4).Text("Luogo e data")
                                .FontSize(8).FontColor(PdfPalette.GreyMedium);
                        });
                        #endregion

                        row.ConstantItem(230).BorderLeft(3).BorderColor(templateStyle.AccentColor)
                            .Background(PdfPalette.GreyLighten3).Padding(12).Column(totCol =>
                        {
                            bool hasDiscount = (ctx.MaterialDiscount + ctx.LaborDiscount) > 0;

                            switch (QuoteCalculator.NormalizeIvaType(ctx.IvaType))
                            {
                                case "RC 10%+22%":
                                    if (totals.Imponibile10 > 0)
                                        totCol.Item().Row(r => { r.RelativeItem().Text("Imponibile al 10%:"); var t = r.RelativeItem().AlignRight().Text($"{totals.Imponibile10:N2} €"); if (hasDiscount) t.FontColor(PdfPalette.RedDarken2); });
                                    if (totals.Imponibile22 > 0)
                                        totCol.Item().Row(r => { r.RelativeItem().Text("Imponibile al 22%:"); var t = r.RelativeItem().AlignRight().Text($"{totals.Imponibile22:N2} €"); if (hasDiscount) t.FontColor(PdfPalette.RedDarken2); });
                                    totCol.Item().PaddingVertical(2).LineHorizontal(0.5f).LineColor(PdfPalette.GreyLighten2);
                                    if (totals.Iva10 > 0)
                                        totCol.Item().Row(r => { r.RelativeItem().Text("IVA al 10%:"); r.RelativeItem().AlignRight().Text($"{totals.Iva10:N2} €"); });
                                    if (totals.Iva22 > 0)
                                        totCol.Item().Row(r => { r.RelativeItem().Text("IVA al 22%:"); r.RelativeItem().AlignRight().Text($"{totals.Iva22:N2} €"); });
                                    break;
                                case "10%":
                                    totCol.Item().Row(r => { r.RelativeItem().Text("Imponibile Totale:"); var t = r.RelativeItem().AlignRight().Text($"{totals.Imponibile:N2} €"); if (hasDiscount) t.FontColor(PdfPalette.RedDarken2); });
                                    totCol.Item().PaddingVertical(2).LineHorizontal(0.5f).LineColor(PdfPalette.GreyLighten2);
                                    totCol.Item().Row(r => { r.RelativeItem().Text($"IVA ({ctx.IvaType}):"); r.RelativeItem().AlignRight().Text($"{totals.IvaTotale:N2} €"); });
                                    break;
                                case "22%":
                                    totCol.Item().Row(r => { r.RelativeItem().Text("Imponibile Totale (22%):"); var t = r.RelativeItem().AlignRight().Text($"{totals.Imponibile22:N2} €"); if (hasDiscount) t.FontColor(PdfPalette.RedDarken2); });
                                    totCol.Item().PaddingVertical(2).LineHorizontal(0.5f).LineColor(PdfPalette.GreyLighten2);
                                    totCol.Item().Row(r => { r.RelativeItem().Text("IVA al 22%:"); r.RelativeItem().AlignRight().Text($"{totals.Iva22:N2} €"); });
                                    break;
                            }

                            string totaleText = QuoteCalculator.NormalizeIvaType(ctx.IvaType) == "esclusa"
                                ? "TOTALE PREVENTIVO - IVA esclusa"
                                : "TOTALE PREVENTIVO - IVA inclusa";

                            totCol.Item().PaddingTop(7).BorderTop(1).Column(total =>
                            {
                                total.Item().Text(totaleText).ExtraBold().FontSize(9).FontColor(PdfPalette.GreyDarken3);
                                total.Item().PaddingTop(3).AlignRight().Text($"{totals.TotaleGenerale:N2} €")
                                    .ExtraBold().FontSize(15).FontColor(templateStyle.AccentColor);
                            });
                        });
                    });

                });

                page.Footer().Column(col =>
                {
                    col.Item().PaddingTop(20).AlignCenter().Text(x =>
                    {
                        x.Span("Pagina "); x.CurrentPageNumber(); x.Span(" di "); x.TotalPages();
                    });
                    if (!string.IsNullOrWhiteSpace(ctx.PdfFooterText))
                        col.Item().PaddingTop(4).AlignCenter().Text(ctx.PdfFooterText).FontSize(8).Italic().FontColor(PdfPalette.GreyMedium);
                });
            });
        }).GeneratePdf(filePath);
    }

    public void GenerateInstallationCertificate(
        InstallationCertificateContext ctx,
        Company company,
        string filePath)
    {
        if (ctx.Materials.Count == 0)
            throw new InvalidOperationException("Il preventivo non contiene materiali da certificare.");

        QuestPDF.Settings.License = LicenseType.Community;
        var templateStyle = PdfTemplateStyle.Resolve(ctx.PdfTemplateName);
        var customer = ctx.AllCustomers.FirstOrDefault(c =>
            string.Equals(c.BusinessName, ctx.CustomerName, StringComparison.OrdinalIgnoreCase));
        string assetsPath = ResolveAssetsDirectory();
        string logoPath = string.IsNullOrWhiteSpace(ctx.SelectedLogo)
            ? string.Empty
            : Path.Combine(assetsPath, ctx.SelectedLogo);
        string stampPath = ResolveStampPath(assetsPath);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(templateStyle.MarginCm, Unit.Centimetre);
                page.PageColor(PdfPalette.White);
                page.DefaultTextStyle(x => x.FontSize(templateStyle.BodyFontSize).FontFamily("Segoe UI"));

                page.Header().ShowOnce().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        if (File.Exists(logoPath))
                            col.Item().Height(60).Image(Image.FromFile(logoPath)).FitHeight();
                        else
                            col.Item().Height(60);

                        col.Item().PaddingTop(10).Text(company.Nome)
                            .FontSize(14).Bold().FontColor(templateStyle.AccentColor);
                        col.Item().Text(company.Indirizzo).FontSize(9).FontColor(PdfPalette.GreyDarken2);
                        col.Item().Text($"P.IVA: {company.Piva}").FontSize(9).FontColor(PdfPalette.GreyDarken2);
                        col.Item().Text($"Email: {company.Email}").FontSize(9).FontColor(PdfPalette.GreyDarken2);
                    });

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().AlignRight().Text("CERTIFICATO")
                            .FontSize(templateStyle.TitleFontSize + 2).ExtraBold().FontColor(PdfPalette.GreyDarken3);
                        col.Item().PaddingTop(3).AlignRight().Text("DI CORRETTA POSA IN OPERA")
                            .FontSize(13).ExtraBold().FontColor(templateStyle.AccentColor);
                        col.Item().PaddingTop(5).AlignRight().Width(245)
                            .LineHorizontal(1.2f).LineColor(templateStyle.AccentColor);
                        col.Item().PaddingTop(7).AlignRight().Text($"Rif. preventivo n. {ctx.QuoteNumber}")
                            .FontSize(10).SemiBold();
                    });
                });

                page.Content().PaddingVertical(25).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Row(section =>
                    {
                        section.ConstantItem(26).Height(22).Background(templateStyle.AccentColor)
                            .AlignCenter().AlignMiddle().Text("01").FontSize(9).Bold().FontColor(PdfPalette.White);
                        section.RelativeItem().PaddingLeft(9).AlignMiddle().Text("DATI DELL'INTERVENTO")
                            .FontSize(10).ExtraBold().FontColor(PdfPalette.GreyDarken3);
                    });

                    col.Item().BorderLeft(4).BorderColor(templateStyle.AccentColor)
                        .Background(PdfPalette.GreyLighten3).Padding(14).Row(info =>
                        {
                            info.RelativeItem().Column(clientInfo =>
                            {
                                clientInfo.Spacing(5);
                                clientInfo.Item().Text("COMMITTENTE")
                                    .FontSize(8).SemiBold().FontColor(PdfPalette.GreyDarken1);
                                clientInfo.Item().Text(ctx.CustomerName)
                                    .FontSize(11).Bold().FontColor(PdfPalette.GreyDarken3);
                                if (!string.IsNullOrWhiteSpace(customer?.Address))
                                    clientInfo.Item().Text(customer.Address).FontSize(9);
                                if (!string.IsNullOrWhiteSpace(ctx.ReferenceName))
                                {
                                    clientInfo.Item().PaddingTop(5).Text("RIFERIMENTO")
                                        .FontSize(8).SemiBold().FontColor(PdfPalette.GreyDarken1);
                                    clientInfo.Item().Text(ctx.ReferenceName)
                                        .FontSize(10).Bold().FontColor(templateStyle.AccentColor);
                                }
                            });

                            info.ConstantItem(20);
                            info.RelativeItem().BorderLeft(1).BorderColor(PdfPalette.GreyLighten1)
                                .PaddingLeft(14).Column(workInfo =>
                            {
                                workInfo.Spacing(5);
                                workInfo.Item().Text("CANTIERE PRESSO")
                                    .FontSize(8).SemiBold().FontColor(PdfPalette.GreyDarken1);
                                workInfo.Item().Text(ctx.WorkSite)
                                    .FontSize(10).Bold().FontColor(PdfPalette.GreyDarken3);
                                workInfo.Item().PaddingTop(5).Text("DATA DI FINE LAVORI")
                                    .FontSize(8).SemiBold().FontColor(PdfPalette.GreyDarken1);
                                workInfo.Item().Text(ctx.CompletionDate.ToString("dd/MM/yyyy"))
                                    .FontSize(10).Bold();
                            });
                        });

                    col.Item().PaddingTop(2).Row(section =>
                    {
                        section.ConstantItem(26).Height(22).Background(templateStyle.AccentColor)
                            .AlignCenter().AlignMiddle().Text("02").FontSize(9).Bold().FontColor(PdfPalette.White);
                        section.RelativeItem().PaddingLeft(9).AlignMiddle().Text("DICHIARAZIONE")
                            .FontSize(10).ExtraBold().FontColor(PdfPalette.GreyDarken3);
                    });

                    col.Item().Border(1).BorderColor(PdfPalette.GreyLighten1).Padding(14).Column(declaration =>
                    {
                        declaration.Item().AlignCenter().Text("DICHIARA E CERTIFICA")
                            .FontSize(12).ExtraBold().FontColor(templateStyle.AccentColor);
                        declaration.Item().PaddingTop(4).AlignCenter().Width(150)
                            .LineHorizontal(1).LineColor(templateStyle.AccentColor);
                        declaration.Item().PaddingTop(10).Text(text =>
                        {
                            text.Span("La sottoscritta impresa ");
                            text.Span(company.Nome).Bold();
                            text.Span(", con riferimento ai lavori eseguiti presso il cantiere sopra indicato, certifica che i materiali elencati nel presente documento sono stati posati in opera ");
                            text.Span("a regola d'arte").Bold();
                            text.Span(", nel rispetto delle indicazioni dei produttori e delle norme tecniche applicabili.");
                        });
                    });

                    col.Item().PaddingTop(2).Row(section =>
                    {
                        section.ConstantItem(26).Height(22).Background(templateStyle.AccentColor)
                            .AlignCenter().AlignMiddle().Text("03").FontSize(9).Bold().FontColor(PdfPalette.White);
                        section.RelativeItem().PaddingLeft(9).AlignMiddle().Text("MATERIALI POSATI")
                            .FontSize(10).ExtraBold().FontColor(PdfPalette.GreyDarken3);
                    });

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.ConstantColumn(58);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(PdfPalette.GreyLighten3).Padding(7)
                                .Text("Materiale / descrizione").SemiBold();
                            header.Cell().Background(PdfPalette.GreyLighten3).Padding(7).AlignCenter()
                                .Text("Quantita'").SemiBold();
                        });

                        foreach (var material in ctx.Materials.OrderBy(m => m.SortOrder))
                        {
                            table.Cell().Element(MaterialRow).Column(materialCol =>
                            {
                                materialCol.Item().Text(material.Name).SemiBold();
                                if (!string.IsNullOrWhiteSpace(material.Description))
                                    materialCol.Item().Text(material.Description)
                                        .FontSize(8).FontColor(PdfPalette.GreyDarken1);
                            });
                            table.Cell().Element(MaterialRow).AlignCenter().AlignMiddle()
                                .Text(material.Quantity.ToString());
                        }

                        static IContainer MaterialRow(IContainer container) =>
                            container.BorderBottom(0.5f)
                                .BorderColor(PdfPalette.GreyLighten2)
                                .PaddingVertical(7)
                                .PaddingHorizontal(5);
                    });

                    col.Item().PaddingTop(8).AlignRight().Width(255).Column(signature =>
                    {
                        signature.Item().AlignCenter().Text("Timbro e firma della ditta")
                            .FontSize(8).SemiBold().FontColor(PdfPalette.GreyDarken2);
                        if (File.Exists(stampPath))
                            signature.Item().PaddingTop(3).Height(52)
                                .Image(Image.FromFile(stampPath)).FitArea();
                        else
                            signature.Item().PaddingTop(45).LineHorizontal(0.7f)
                                .LineColor(PdfPalette.GreyDarken1);
                    });
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    string footer = string.IsNullOrWhiteSpace(ctx.FooterText)
                        ? $"{company.Nome} - {company.Piva}"
                        : ctx.FooterText;
                    text.Span(footer + "  |  ").FontSize(8).FontColor(PdfPalette.GreyMedium);
                    text.CurrentPageNumber().FontSize(8).FontColor(PdfPalette.GreyMedium);
                    text.Span(" / ").FontSize(8).FontColor(PdfPalette.GreyMedium);
                    text.TotalPages().FontSize(8).FontColor(PdfPalette.GreyMedium);
                });
            });
        }).GeneratePdf(filePath);
    }

    private static string ResolveAssetsDirectory()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "Assets"),
            Path.Combine(baseDir, "assets"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Assets")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "assets"))
        ];

        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    private static string ResolveStampPath(string assetsPath)
    {
        if (!Directory.Exists(assetsPath))
            return string.Empty;

        string[] preferredNames = ["timbro.png", "timbro.jpeg", "timbro.jpg"];
        var files = Directory.EnumerateFiles(assetsPath).ToList();
        foreach (string preferredName in preferredNames)
        {
            string? preferred = files.FirstOrDefault(path =>
                string.Equals(Path.GetFileName(path), preferredName, StringComparison.OrdinalIgnoreCase));
            if (preferred != null)
                return preferred;
        }

        return files.FirstOrDefault(path =>
                   Path.GetFileNameWithoutExtension(path).StartsWith("Timbro", StringComparison.OrdinalIgnoreCase) &&
                   new[] { ".png", ".jpg", ".jpeg" }.Contains(
                       Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
               ?? string.Empty;
    }

    /// <summary>
    /// Genera il PDF interno dei costi di collaborazione (uso interno, non va al cliente).
    /// </summary>
    public void GenerateCostsPdf(CostsPdfContext ctx, Company company, string filePath)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        double ourTotal = ctx.OurCosts.Sum(c => c.Amount);
        double partnerTotal = ctx.PartnerCosts.Sum(c => c.Amount);
        double additionalTotal = ctx.AdditionalCosts.Sum(c => c.Amount);
        double grandTotal = ourTotal + partnerTotal + additionalTotal;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.PageColor(PdfPalette.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Segoe UI"));

                page.Header().ShowOnce().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(company.Nome).FontSize(13).Bold().FontColor(PdfPalette.AccentRed);
                            c.Item().Text("DOCUMENTO INTERNO — RIPARTIZIONE COSTI").FontSize(11).Bold().FontColor(PdfPalette.GreyDarken3);
                            c.Item().Text($"Preventivo n. {ctx.QuoteNumber}  —  {ctx.Date:dd/MM/yyyy}").FontSize(9).FontColor(PdfPalette.GreyMedium);
                        });
                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text("USO INTERNO").FontSize(16).Bold().FontColor(PdfPalette.OrangeMedium);
                            c.Item().AlignRight().Text($"Cliente: {ctx.CustomerName}").FontSize(9).FontColor(PdfPalette.GreyDarken2);
                            if (!string.IsNullOrWhiteSpace(ctx.PartnerCompanyName))
                                c.Item().AlignRight().Text($"Partner: {ctx.PartnerCompanyName}").FontSize(9).FontColor(PdfPalette.BlueDarken1);
                        });
                    });
                    col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(PdfPalette.OrangeMedium);
                });

                page.Content().PaddingVertical(20).Column(col =>
                {
                    static IContainer SectionHeader(IContainer c) =>
                        c.Background(PdfPalette.GreyLighten3).Padding(6).BorderLeft(3).BorderColor(PdfPalette.AccentRed);
                    static IContainer RowCell(IContainer c) =>
                        c.BorderBottom(0.5f).BorderColor(PdfPalette.GreyLighten2).PaddingVertical(5).PaddingHorizontal(4);

                    void RenderSection(string title, IEnumerable<CostAllocationItem> items, double total)
                    {
                        col.Item().PaddingBottom(4).Element(SectionHeader)
                           .Text(title).Bold().FontSize(11);

                        col.Item().PaddingBottom(10).Table(table =>
                        {
                            table.ColumnsDefinition(cd =>
                            {
                                cd.RelativeColumn(4);
                                cd.ConstantColumn(100);
                                cd.RelativeColumn(2);
                            });

                            table.Cell().Element(RowCell).Text("Descrizione").Bold().FontSize(9).FontColor(PdfPalette.GreyDarken2);
                            table.Cell().Element(RowCell).AlignRight().Text("Importo (€)").Bold().FontSize(9).FontColor(PdfPalette.GreyDarken2);
                            table.Cell().Element(RowCell).Text("Note").Bold().FontSize(9).FontColor(PdfPalette.GreyDarken2);

                            var itemList = items.ToList();
                            if (itemList.Count == 0)
                            {
                                table.Cell().ColumnSpan(3).Padding(8).Text("— nessuna voce —").Italic().FontColor(PdfPalette.GreyMedium);
                            }
                            else
                            {
                                foreach (var item in itemList)
                                {
                                    table.Cell().Element(RowCell).Text(item.Description);
                                    table.Cell().Element(RowCell).AlignRight().Text($"{item.Amount:N2} €");
                                    table.Cell().Element(RowCell).Text(item.Notes).FontSize(9).FontColor(PdfPalette.GreyDarken1);
                                }
                            }

                            table.Cell().Element(c => c.PaddingVertical(4)).Text("Subtotale").Bold();
                            table.Cell().Element(c => c.PaddingVertical(4)).AlignRight().Text($"{total:N2} €").Bold().FontColor(PdfPalette.GreenDarken1);
                            table.Cell();
                        });
                    }

                    RenderSection("🏢 Nostri Costi (EdilPaint)", ctx.OurCosts, ourTotal);
                    RenderSection($"🤝 Costi Ditta Partner ({(string.IsNullOrWhiteSpace(ctx.PartnerCompanyName) ? "—" : ctx.PartnerCompanyName)})", ctx.PartnerCosts, partnerTotal);
                    RenderSection("➕ Costi Aggiuntivi / Condivisi", ctx.AdditionalCosts, additionalTotal);

                    col.Item().PaddingTop(10).BorderTop(2).BorderColor(PdfPalette.GreyDarken2).Row(row =>
                    {
                        row.RelativeItem().Text("TOTALE COSTI INTERNI").Bold().FontSize(13);
                        row.ConstantItem(160).AlignRight().Text($"{grandTotal:N2} €").Bold().FontSize(14).FontColor(PdfPalette.GreenDarken2);
                    });

                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text("Totale preventivo cliente (IVA inclusa)").FontSize(10).FontColor(PdfPalette.GreyDarken2);
                        row.ConstantItem(160).AlignRight().Text($"{ctx.Total:N2} €").FontSize(10).FontColor(PdfPalette.GreyDarken2);
                    });

                    col.Item().PaddingTop(2).Row(row =>
                    {
                        row.RelativeItem().Text("Imponibile preventivo cliente").FontSize(10).FontColor(PdfPalette.GreyDarken2);
                        row.ConstantItem(160).AlignRight().Text($"{ctx.Imponibile:N2} €").FontSize(10).FontColor(PdfPalette.GreyDarken2);
                    });

                    double margin = CalculateEstimatedMargin(ctx);
                    col.Item().PaddingTop(2).Row(row =>
                    {
                        row.RelativeItem().Text("Margine stimato").FontSize(10).Bold();
                        row.ConstantItem(160).AlignRight()
                           .Text($"{margin:N2} €").FontSize(11).Bold()
                           .FontColor(margin >= 0 ? PdfPalette.GreenDarken2 : PdfPalette.RedDarken2);
                    });
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Pagina "); x.CurrentPageNumber(); x.Span(" di "); x.TotalPages();
                    x.Span("    —    Documento riservato, uso interno esclusivo").FontSize(8).Italic().FontColor(PdfPalette.GreyMedium);
                });
            });
        }).GeneratePdf(filePath);
    }

    private static void NormalizePdfTemplate(PdfGenerationContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.PdfTemplateName))
            ctx.PdfTemplateName = "Standard";
        if (string.IsNullOrWhiteSpace(ctx.PdfNotesTitle))
            ctx.PdfNotesTitle = "NOTE E TERMINI DI PAGAMENTO";
        if (string.IsNullOrWhiteSpace(ctx.PdfSignatureText))
            ctx.PdfSignatureText = "Firma per accettazione";
        ctx.PdfFooterText ??= string.Empty;
    }
}
