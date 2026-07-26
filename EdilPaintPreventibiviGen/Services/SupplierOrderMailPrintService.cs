using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public static class SupplierOrderMailPrintService
{
    public static bool Print(
        QuoteHistoryEntry quote,
        SupplierOrderMailDraft draft,
        DateTime registeredAtUtc)
    {
        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() != true)
            return false;

        var document = BuildDocument(quote, draft, registeredAtUtc);
        if (printDialog.PrintableAreaWidth > 0 && printDialog.PrintableAreaHeight > 0)
        {
            document.PageWidth = printDialog.PrintableAreaWidth;
            document.PageHeight = printDialog.PrintableAreaHeight;
            document.ColumnWidth = printDialog.PrintableAreaWidth;
        }

        printDialog.PrintDocument(
            ((IDocumentPaginatorSource)document).DocumentPaginator,
            $"Ordine materiale {quote.QuoteNumber}");
        return true;
    }

    private static FlowDocument BuildDocument(
        QuoteHistoryEntry quote,
        SupplierOrderMailDraft draft,
        DateTime registeredAtUtc)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            Foreground = Brushes.Black,
            PagePadding = new Thickness(48),
            ColumnGap = 0
        };

        document.Blocks.Add(new Paragraph(new Run("Ordine materiale"))
        {
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 14)
        });

        AddField(document, "Preventivo", quote.QuoteNumber);
        AddField(document, "Registrato il", registeredAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
        AddField(document, "Destinatario", draft.Recipient);
        if (!string.IsNullOrWhiteSpace(draft.CcRecipients))
            AddField(document, "Copia", draft.CcRecipients);
        AddField(document, "Oggetto", draft.Subject);

        document.Blocks.Add(new Paragraph(new Run("Testo email"))
        {
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 18, 0, 5)
        });
        document.Blocks.Add(new Paragraph(new Run(draft.Body ?? string.Empty))
        {
            Margin = new Thickness(0),
            LineHeight = 18
        });

        return document;
    }

    private static void AddField(FlowDocument document, string label, string value)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 5) };
        paragraph.Inlines.Add(new Run(label + ": ") { FontWeight = FontWeights.SemiBold });
        paragraph.Inlines.Add(new Run(value ?? string.Empty));
        document.Blocks.Add(paragraph);
    }
}
