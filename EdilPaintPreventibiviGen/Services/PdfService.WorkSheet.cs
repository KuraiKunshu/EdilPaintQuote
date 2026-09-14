using System.Collections.Generic;
using System.IO;
using EdilPaintPreventibiviGen.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EdilPaintPreventibiviGen.Services;

public partial class PdfService
{
    private static class WorkSheetPalette
    {
        public static readonly Color AccentRed = Color.FromHex("#B3261E");
        public static readonly Color GreyDarken3 = Color.FromHex("#252525");
        public static readonly Color GreyDarken2 = Color.FromHex("#444444");
        public static readonly Color GreyDarken1 = Color.FromHex("#616161");
        public static readonly Color GreyLighten1 = Color.FromHex("#BDBDBD");
        public static readonly Color GreyLighten2 = Color.FromHex("#E0E0E0");
        public static readonly Color GreyLighten3 = Color.FromHex("#F3F4F5");
        public static readonly Color GreenDarken2 = Color.FromHex("#25652A");
        public static readonly Color BlueDarken1 = Color.FromHex("#1762A8");
        public static readonly Color OrangeMedium = Color.FromHex("#995600");
        public static readonly Color RedDarken2 = Color.FromHex("#B3261E");
    }

    public void GenerateWorkSheet(WorkSheetContext context, Company company, string filePath)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        string logoPath = Path.Combine(ResolveAssetsDirectory(), context.SelectedLogo);

        Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(style => style.FontFamily("Segoe UI").FontSize(10).FontColor(WorkSheetPalette.GreyDarken3));

            page.Header().PaddingBottom(14).BorderBottom(2).BorderColor(WorkSheetPalette.AccentRed).PaddingBottom(10).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    if (File.Exists(logoPath)) left.Item().Width(110).Height(36).Image(logoPath).FitArea();
                    left.Item().Text(company.Nome).FontSize(12).Bold();
                });
                row.RelativeItem().Column(right =>
                {
                    right.Item().AlignRight().Text("SCHEDA LAVORO").FontSize(21).Bold().FontColor(WorkSheetPalette.AccentRed);
                    right.Item().AlignRight().Text($"Preventivo {context.QuoteNumber} del {context.QuoteDate:dd/MM/yyyy}").FontSize(9);
                    right.Item().AlignRight().Text($"Generata il {context.GeneratedAt:dd/MM/yyyy HH:mm}").FontSize(8).FontColor(WorkSheetPalette.GreyDarken1);
                });
            });

            page.Content().Column(column =>
            {
                column.Spacing(12);
                if (context.IsOfflineSnapshot)
                    column.Item().Text("COPIA OFFLINE - Verificare dati e stato del materiale prima dell'intervento.")
                        .Bold().FontColor(WorkSheetPalette.RedDarken2);

                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        Field(left, "CLIENTE", context.CustomerName);
                        if (!string.IsNullOrWhiteSpace(context.ReferenceName)) Field(left, "RIFERIMENTO", context.ReferenceName);
                    });
                    row.ConstantItem(20);
                    row.RelativeItem().Column(right =>
                    {
                        Field(right, "CANTIERE", Empty(context.WorkSite));
                        Field(right, "TELEFONO", Empty(context.ContactPhone));
                    });
                });

                column.Item().Row(row =>
                {
                    row.RelativeItem().Text("Data intervento: __________________");
                    row.RelativeItem().Text("Squadra: ________________________");
                });

                WorkSheetNotes(column, "NOTE", context.CustomerNotes);
                WorkSheetNotes(column, "NOTE INTERNE", context.Notes);

                if (context.Materials.Count > 0)
                {
                    column.Item().EnsureSpace(105).Column(materialInfo =>
                    {
                        Section(materialInfo, "MATERIALI");
                        MaterialSummary(materialInfo, context);
                    });
                    column.Item().Element(container => WorkLines(
                        container,
                        context.Materials,
                        checkboxes: false,
                        itemHeader: "Materiale"));
                }

                column.Item().EnsureSpace(70).Column(labors =>
                {
                    Section(labors, "LAVORAZIONI DA ESEGUIRE");
                    if (context.Labors.Count == 0) labors.Item().PaddingTop(8).Text("Nessuna lavorazione da riportare nella scheda.");
                });
                if (context.Labors.Count > 0)
                    column.Item().Element(container => WorkLines(
                        container,
                        context.Labors,
                        checkboxes: true,
                        itemHeader: "Lavorazione / descrizione"));

                column.Item().EnsureSpace(115).Column(notes =>
                {
                    Section(notes, "NOTE DI INTERVENTO");
                    for (int i = 0; i < 3; i++)
                        notes.Item().Height(22).AlignBottom().LineHorizontal(0.5f).LineColor(WorkSheetPalette.GreyLighten1);
                    notes.Item().PaddingTop(12).Text("Fine lavori: __________________   Operatore: __________________________").FontSize(9);
                });
            });

            page.Footer().PaddingTop(12).Row(row =>
            {
                row.RelativeItem().Text($"Scheda lavoro - {context.QuoteNumber}").FontSize(8).FontColor(WorkSheetPalette.GreyDarken1);
                row.AutoItem().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(8));
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        })).GeneratePdf(filePath);

        static string Empty(string value) => string.IsNullOrWhiteSpace(value) ? "Non indicato" : value;
    }

    private static void Field(ColumnDescriptor column, string label, string value)
    {
        column.Item().PaddingTop(5).Text(label).FontSize(8).FontColor(WorkSheetPalette.GreyDarken1);
        column.Item().Text(value).FontSize(11).SemiBold();
    }

    private static void WorkSheetNotes(ColumnDescriptor column, string title, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        column.Item().EnsureSpace(60).Column(notes =>
        {
            Section(notes, title);
            notes.Item().PaddingTop(6).Text(content);
        });
    }

    private static void MaterialSummary(ColumnDescriptor column, WorkSheetContext context)
    {
        Color stateColor = MaterialStateColor(context.MaterialStatus);
        column.Item().PaddingTop(8)
            .Border(1).BorderColor(stateColor)
            .Background(WorkSheetPalette.GreyLighten3)
            .Padding(8)
            .Column(summary =>
            {
                summary.Item().Text($"STATO MATERIALE: {context.MaterialStatus}")
                    .FontSize(13).Bold().FontColor(stateColor);
                summary.Item().PaddingTop(4).Text(MaterialLocation(context)).FontSize(10).SemiBold();

                string[] dates = MaterialDates(context);
                if (dates.Length > 0)
                {
                    summary.Item().PaddingTop(4).Text(string.Join("   |   ", dates))
                        .FontSize(9).FontColor(WorkSheetPalette.GreyDarken1);
                }
            });
    }

    private static string MaterialLocation(WorkSheetContext context)
    {
        if (context.MaterialsOrderedByCustomer)
        {
            string customerName = string.IsNullOrWhiteSpace(context.CustomerName)
                ? "Non indicato"
                : context.CustomerName;
            return $"POSIZIONE: gestito dal cliente - {customerName}";
        }

        return string.IsNullOrWhiteSpace(context.SupplierName)
            ? "POSIZIONE: non indicata"
            : $"POSIZIONE: presso il fornitore - {context.SupplierName.Trim()}";
    }

    private static string[] MaterialDates(WorkSheetContext context)
    {
        var dates = new List<string>(2);
        if (context.MaterialOrderDate.HasValue)
            dates.Add($"ORDINATO IL {context.MaterialOrderDate.Value:dd/MM/yyyy}");
        if (context.ExpectedDeliveryDate.HasValue)
            dates.Add($"CONSEGNA PREVISTA {context.ExpectedDeliveryDate.Value:dd/MM/yyyy}");
        return dates.ToArray();
    }

    private static void Section(ColumnDescriptor column, string title) =>
        column.Item().BorderBottom(1).BorderColor(WorkSheetPalette.GreyLighten1).PaddingBottom(5)
            .Text(title).FontSize(11).Bold().FontColor(WorkSheetPalette.AccentRed);

    private static Color MaterialStateColor(string status) => status.ToUpperInvariant() switch
    {
        "CONSEGNATO" or "IN MAGAZZINO" => WorkSheetPalette.GreenDarken2,
        "ORDINATO" => WorkSheetPalette.BlueDarken1,
        "DA ORDINARE" or "DA RITIRARE" => WorkSheetPalette.OrangeMedium,
        "NON DISPONIBILE" => WorkSheetPalette.RedDarken2,
        _ => WorkSheetPalette.GreyDarken3
    };

    private static void WorkLines(
        IContainer container,
        IReadOnlyList<WorkSheetLine> lines,
        bool checkboxes,
        string itemHeader)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                if (checkboxes) columns.ConstantColumn(38);
                columns.ConstantColumn(50);
                columns.RelativeColumn();
            });
            table.Header(header =>
            {
                if (checkboxes) header.Cell().Element(Head).Text("Fatto");
                header.Cell().Element(Head).Text("Q.ta");
                header.Cell().Element(Head).Text(itemHeader);
            });
            foreach (WorkSheetLine line in lines)
            {
                // Keep quantity, checkbox and description together at page boundaries.
                table.Cell().ColumnSpan(checkboxes ? 3u : 2u).PreventPageBreak()
                    .BorderBottom(0.5f).BorderColor(WorkSheetPalette.GreyLighten2).Row(row =>
                {
                    if (checkboxes) row.ConstantItem(38).Padding(6).PaddingTop(2).Width(12).Height(12)
                        .Border(0.8f).BorderColor(WorkSheetPalette.GreyDarken2);
                    row.ConstantItem(50).Padding(6).Text(line.Quantity.ToString()).FontSize(12).Bold();
                    row.RelativeItem().Padding(6).Column(description =>
                    {
                        description.Item().Text(line.Name).SemiBold();
                        if (!string.IsNullOrWhiteSpace(line.Description))
                            description.Item().PaddingTop(3).Text(line.Description).FontSize(9).FontColor(WorkSheetPalette.GreyDarken1);
                    });
                });
            }
        });

        static IContainer Head(IContainer cell) => cell.Background(WorkSheetPalette.GreyLighten3).Padding(6).DefaultTextStyle(style => style.SemiBold().FontSize(9));
    }
}
