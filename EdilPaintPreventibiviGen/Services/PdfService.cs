using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using EdilPaintPreventibiviGen.ViewModels;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public class PdfService
{
    /// <summary>
    /// Punto di ingresso per la generazione da UI (MainViewModel).
    /// Converte il ViewModel in PdfGenerationContext e delega.
    /// </summary>
    public void GenerateQuote(MainViewModel vm, Company company, string filePath)
    {
        var ctx = new PdfGenerationContext
        {
            QuoteNumber = vm.QuoteNumber,
            Date = DateTime.Now,
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
            AllCustomers = vm.AllCustomers.ToList()
        };

        GenerateQuoteFromContext(ctx, company, filePath);
    }

    /// <summary>
    /// Metodo principale di generazione PDF — non dipende dal ViewModel.
    /// </summary>
    public void GenerateQuoteFromContext(PdfGenerationContext ctx, Company company, string filePath)
    {
        QuestPDF.Settings.License = LicenseType.Community;

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
                page.Margin(1.5f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Segoe UI"));

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

                        col.Item().PaddingTop(10).Text(company.Nome).FontSize(14).Bold().FontColor(Colors.Red.Medium);
                        col.Item().Text(company.Indirizzo).FontSize(9).FontColor(Colors.Grey.Darken2);
                        col.Item().Text($"P.IVA: {company.Piva}").FontSize(9).FontColor(Colors.Grey.Darken2);
                        col.Item().Text($"Email: {company.Email}").FontSize(9).FontColor(Colors.Grey.Darken2);
                    });

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().AlignRight().Text("PREVENTIVO").FontSize(22).ExtraBold().FontColor(Colors.Grey.Lighten2);
                        col.Item().AlignRight().Text($"n. {ctx.QuoteNumber}").FontSize(16).Bold();

                        var dataScadenza = ctx.Date.AddMonths(1).AddDays(15);
                        col.Item().AlignRight().Text(ctx.Date.ToString("dd MMMM yyyy")).FontSize(10).Italic();
                        col.Item().AlignRight().Text($"Valido fino al: {dataScadenza:dd/MM/yyyy}").FontSize(9).FontColor(Colors.Grey.Medium);

                        if (customer != null)
                        {
                            col.Item().PaddingTop(20).AlignRight().Column(custCol =>
                            {
                                custCol.Item().Text("Spettabile").FontSize(8).Italic().FontColor(Colors.Grey.Medium);
                                custCol.Item().Text(customer.BusinessName).Bold().FontSize(12);
                                custCol.Item().Text(customer.Address).FontSize(10);
                                if (!string.IsNullOrWhiteSpace(customer.Phone))
                                    custCol.Item().Text($"Tel: {customer.Phone}").FontSize(9).FontColor(Colors.Grey.Darken2);
                                if (!string.IsNullOrWhiteSpace(customer.Email))
                                    custCol.Item().Text($"Mail: {customer.Email}").FontSize(9).FontColor(Colors.Grey.Darken2);

                                if (reference != null)
                                {
                                    custCol.Item().PaddingTop(10).Text("Riferimento:").FontSize(8).Italic().FontColor(Colors.Grey.Medium);
                                    custCol.Item().Text(reference.BusinessName).Bold().FontSize(11).FontColor(Colors.Red.Medium);
                                    custCol.Item().Text(reference.Address).FontSize(9);
                                    if (!string.IsNullOrWhiteSpace(reference.Phone))
                                        custCol.Item().Text($"Tel: {reference.Phone}").FontSize(9).FontColor(Colors.Grey.Darken2);
                                    if (!string.IsNullOrWhiteSpace(reference.Email))
                                        custCol.Item().Text($"Mail: {reference.Email}").FontSize(9).FontColor(Colors.Grey.Darken2);
                                }
                            });
                        }
                    });
                });
                #endregion

                page.Content().PaddingVertical(25).Column(col =>
                {
                    #region Materiali
                    if (ctx.Materials.Count > 0)
                    {
                        col.Item().BorderBottom(1).BorderColor(Colors.Red.Medium).PaddingBottom(5)
                           .Text("MATERIALI").FontSize(11).Bold().FontColor(Colors.Red.Medium);

                        col.Item().PaddingBottom(15).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3); columns.ConstantColumn(40);
                                columns.ConstantColumn(80); columns.ConstantColumn(80);
                            });
                            foreach (var item in ctx.Materials)
                            {
                                table.Cell().Element(RowStyle).Column(c =>
                                {
                                    c.Item().Text(text =>
                                    {
                                        text.Span(item.Name).Bold();
                                        if (item.IsSignificant)
                                            text.Span(" [*]").FontSize(8).FontColor(Colors.Red.Medium).Italic();
                                    });
                                    if (!string.IsNullOrWhiteSpace(item.Description))
                                        c.Item().Text(item.Description).FontSize(8).FontColor(Colors.Grey.Darken1);
                                });
                                table.Cell().Element(RowStyle).AlignCenter().Text(item.Quantity.ToString());
                                table.Cell().Element(RowStyle).AlignRight().Text(text =>
                                {
                                    text.Line($"{item.UnitPrice:N2} €");
                                    double totalDiscount = item.Discount + ctx.MaterialDiscount;
                                    if (totalDiscount > 0)
                                        text.Line($"(-{totalDiscount}%)").FontSize(8).FontColor(Colors.Grey.Medium);
                                });
                                table.Cell().Element(RowStyle).AlignRight().Text($"{item.TotalPrice * (1 - ctx.MaterialDiscount / 100):N2} €");
                                static IContainer RowStyle(IContainer c) => c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).PaddingVertical(5);
                            }
                        });
                    }
                    #endregion

                    #region Lavorazioni
                    if (ctx.Labors.Count > 0)
                    {
                        col.Item().BorderBottom(1).BorderColor(Colors.Red.Medium).PaddingBottom(5)
                           .Text("VOCI MANODOPERA").FontSize(11).Bold().FontColor(Colors.Red.Medium);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3); columns.ConstantColumn(40);
                                columns.ConstantColumn(80); columns.ConstantColumn(80);
                            });
                            foreach (var item in ctx.Labors)
                            {
                                table.Cell().Element(RowStyle).Column(c =>
                                {
                                    c.Item().Text(text =>
                                    {
                                        text.Span(item.Name).Bold();
                                        if (item.IsSignificant)
                                            text.Span(" [*]").FontSize(8).FontColor(Colors.Red.Medium).Italic();
                                    });
                                    if (!string.IsNullOrWhiteSpace(item.Description))
                                        c.Item().Text(item.Description).FontSize(8).FontColor(Colors.Grey.Darken1);
                                });
                                table.Cell().Element(RowStyle).AlignCenter().Text(item.Quantity.ToString());
                                table.Cell().Element(RowStyle).AlignRight().Text(text =>
                                {
                                    double totalDiscount = item.Discount + ctx.LaborDiscount;
                                    text.Line($"{item.UnitPrice:N2} €");
                                    if (totalDiscount > 0)
                                        text.Line($"(-{totalDiscount}%)").FontSize(8).FontColor(Colors.Grey.Medium);
                                });
                                table.Cell().Element(RowStyle).AlignRight().Text($"{item.TotalPrice * (1 - ctx.LaborDiscount / 100):N2} €");
                                static IContainer RowStyle(IContainer c) => c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).PaddingVertical(5);
                            }
                        });
                    }
                    #endregion

                    col.Item().PaddingTop(30).BorderTop(1).BorderColor(Colors.Grey.Lighten1).Row(row =>
                    {
                        #region Note
                        row.RelativeItem().PaddingRight(30).Column(noteCol =>
                        {
                            noteCol.Item().Text("NOTE E TERMINI DI PAGAMENTO").FontSize(9).Bold();
                            noteCol.Item().PaddingTop(5).PaddingBottom(15).Text(ctx.PaymentTerms).FontSize(9).LineHeight(1.2f);
                        });
                        #endregion

                        row.ConstantItem(230).Column(totCol =>
                        {
                            bool hasDiscount = (ctx.MaterialDiscount + ctx.LaborDiscount) > 0;

                            switch (ctx.IvaType)
                            {
                                case "RC 10%+22%":
                                    if (totals.Imponibile10 > 0)
                                        totCol.Item().Row(r => { r.RelativeItem().Text("Imponibile al 10%:"); var t = r.RelativeItem().AlignRight().Text($"{totals.Imponibile10:N2} €"); if (hasDiscount) t.FontColor(Colors.Green.Darken1); });
                                    if (totals.Imponibile22 > 0)
                                        totCol.Item().Row(r => { r.RelativeItem().Text("Imponibile al 22%:"); var t = r.RelativeItem().AlignRight().Text($"{totals.Imponibile22:N2} €"); if (hasDiscount) t.FontColor(Colors.Green.Darken1); });
                                    totCol.Item().PaddingVertical(2).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                                    if (totals.Iva10 > 0)
                                        totCol.Item().Row(r => { r.RelativeItem().Text("IVA al 10%:"); r.RelativeItem().AlignRight().Text($"{totals.Iva10:N2} €"); });
                                    if (totals.Iva22 > 0)
                                        totCol.Item().Row(r => { r.RelativeItem().Text("IVA al 22%:"); r.RelativeItem().AlignRight().Text($"{totals.Iva22:N2} €"); });
                                    break;
                                case "10%":
                                    totCol.Item().Row(r => { r.RelativeItem().Text("Imponibile Totale:"); var t = r.RelativeItem().AlignRight().Text($"{totals.Imponibile:N2} €"); if (hasDiscount) t.FontColor(Colors.Green.Darken1); });
                                    totCol.Item().PaddingVertical(2).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                                    totCol.Item().Row(r => { r.RelativeItem().Text($"IVA ({ctx.IvaType}):"); r.RelativeItem().AlignRight().Text($"{totals.IvaTotale:N2} €"); });
                                    break;
                                case "22%":
                                    if (totals.Imponibile10 > 0)
                                        totCol.Item().Row(r => { r.RelativeItem().Text("Imponibile manodopera (10%):"); var t = r.RelativeItem().AlignRight().Text($"{totals.Imponibile10:N2} €"); if (hasDiscount) t.FontColor(Colors.Green.Darken1); });
                                    if (totals.Imponibile22 > 0)
                                        totCol.Item().Row(r => { r.RelativeItem().Text("Imponibile materiali (22%):"); var t = r.RelativeItem().AlignRight().Text($"{totals.Imponibile22:N2} €"); if (hasDiscount) t.FontColor(Colors.Green.Darken1); });
                                    totCol.Item().PaddingVertical(2).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                                    if (totals.Iva10 > 0)
                                        totCol.Item().Row(r => { r.RelativeItem().Text("IVA manodopera (10%):"); r.RelativeItem().AlignRight().Text($"{totals.Iva10:N2} €"); });
                                    if (totals.Iva22 > 0)
                                        totCol.Item().Row(r => { r.RelativeItem().Text("IVA materiali (22%):"); r.RelativeItem().AlignRight().Text($"{totals.Iva22:N2} €"); });
                                    break;
                            }

                            string totaleText = ctx.IvaType == "esclusa"
                                ? "TOTALE PREVENTIVO (IVA esclusa):"
                                : "TOTALE PREVENTIVO (IVA inclusa):";

                            totCol.Item().PaddingTop(5).BorderTop(1).Row(r =>
                            {
                                r.RelativeItem().Text(totaleText).Bold().FontSize(12);
                                r.RelativeItem().AlignRight().Text($"{totals.TotaleGenerale:N2} €").Bold().FontSize(12).FontColor(Colors.Green.Medium);
                            });
                        });
                    });

                    #region Firma
                    col.Item().PaddingTop(20).EnsureSpace(80).Column(firmaCol =>
                    {
                        firmaCol.Item().PaddingBottom(6).Text("Firma per accettazione").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                        firmaCol.Item().PaddingTop(18).Row(row =>
                        {
                            row.RelativeItem().BorderBottom(1).BorderColor(Colors.Grey.Darken1).Height(18);
                            row.ConstantItem(20);
                            row.RelativeItem().BorderBottom(1).BorderColor(Colors.Grey.Darken1).Height(18);
                        });
                        firmaCol.Item().PaddingTop(4).Text("Luogo e data").FontSize(8).FontColor(Colors.Grey.Medium);
                    });
                    #endregion
                });

                page.Footer().Column(col =>
                {
                    col.Item().PaddingTop(20).AlignCenter().Text(x =>
                    {
                        x.Span("Pagina "); x.CurrentPageNumber(); x.Span(" di "); x.TotalPages();
                    });
                });
            });
        }).GeneratePdf(filePath);
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
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Segoe UI"));

                page.Header().ShowOnce().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(company.Nome).FontSize(13).Bold().FontColor(Colors.Red.Medium);
                            c.Item().Text("DOCUMENTO INTERNO — RIPARTIZIONE COSTI").FontSize(11).Bold().FontColor(Colors.Grey.Darken3);
                            c.Item().Text($"Preventivo n. {ctx.QuoteNumber}  —  {ctx.Date:dd/MM/yyyy}").FontSize(9).FontColor(Colors.Grey.Medium);
                        });
                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text("USO INTERNO").FontSize(16).Bold().FontColor(Colors.Orange.Medium);
                            c.Item().AlignRight().Text($"Cliente: {ctx.CustomerName}").FontSize(9).FontColor(Colors.Grey.Darken2);
                            if (!string.IsNullOrWhiteSpace(ctx.PartnerCompanyName))
                                c.Item().AlignRight().Text($"Partner: {ctx.PartnerCompanyName}").FontSize(9).FontColor(Colors.Blue.Darken1);
                        });
                    });
                    col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(Colors.Orange.Medium);
                });

                page.Content().PaddingVertical(20).Column(col =>
                {
                    static IContainer SectionHeader(IContainer c) =>
                        c.Background(Colors.Grey.Lighten3).Padding(6).BorderLeft(3).BorderColor(Colors.Red.Medium);
                    static IContainer RowCell(IContainer c) =>
                        c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).PaddingHorizontal(4);

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

                            table.Cell().Element(RowCell).Text("Descrizione").Bold().FontSize(9).FontColor(Colors.Grey.Darken2);
                            table.Cell().Element(RowCell).AlignRight().Text("Importo (€)").Bold().FontSize(9).FontColor(Colors.Grey.Darken2);
                            table.Cell().Element(RowCell).Text("Note").Bold().FontSize(9).FontColor(Colors.Grey.Darken2);

                            var itemList = items.ToList();
                            if (itemList.Count == 0)
                            {
                                table.Cell().ColumnSpan(3).Padding(8).Text("— nessuna voce —").Italic().FontColor(Colors.Grey.Medium);
                            }
                            else
                            {
                                foreach (var item in itemList)
                                {
                                    table.Cell().Element(RowCell).Text(item.Description);
                                    table.Cell().Element(RowCell).AlignRight().Text($"{item.Amount:N2} €");
                                    table.Cell().Element(RowCell).Text(item.Notes).FontSize(9).FontColor(Colors.Grey.Darken1);
                                }
                            }

                            table.Cell().Element(c => c.PaddingVertical(4)).Text("Subtotale").Bold();
                            table.Cell().Element(c => c.PaddingVertical(4)).AlignRight().Text($"{total:N2} €").Bold().FontColor(Colors.Green.Darken1);
                            table.Cell();
                        });
                    }

                    RenderSection("🏢 Nostri Costi (EdilPaint)", ctx.OurCosts, ourTotal);
                    RenderSection($"🤝 Costi Ditta Partner ({(string.IsNullOrWhiteSpace(ctx.PartnerCompanyName) ? "—" : ctx.PartnerCompanyName)})", ctx.PartnerCosts, partnerTotal);
                    RenderSection("➕ Costi Aggiuntivi / Condivisi", ctx.AdditionalCosts, additionalTotal);

                    col.Item().PaddingTop(10).BorderTop(2).BorderColor(Colors.Grey.Darken2).Row(row =>
                    {
                        row.RelativeItem().Text("TOTALE COSTI INTERNI").Bold().FontSize(13);
                        row.ConstantItem(160).AlignRight().Text($"{grandTotal:N2} €").Bold().FontSize(14).FontColor(Colors.Green.Darken2);
                    });

                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text("Totale preventivo cliente").FontSize(10).FontColor(Colors.Grey.Darken2);
                        row.ConstantItem(160).AlignRight().Text($"{ctx.Total:N2} €").FontSize(10).FontColor(Colors.Grey.Darken2);
                    });

                    double margin = ctx.Total - grandTotal;
                    col.Item().PaddingTop(2).Row(row =>
                    {
                        row.RelativeItem().Text("Margine stimato").FontSize(10).Bold();
                        row.ConstantItem(160).AlignRight()
                           .Text($"{margin:N2} €").FontSize(11).Bold()
                           .FontColor(margin >= 0 ? Colors.Green.Darken2 : Colors.Red.Darken2);
                    });
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Pagina "); x.CurrentPageNumber(); x.Span(" di "); x.TotalPages();
                    x.Span("    —    Documento riservato, uso interno esclusivo").FontSize(8).Italic().FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf(filePath);
    }
}