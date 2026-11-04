using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

internal static class SupplierOrderSummaryText
{
    public static IEnumerable<Item> GetMaterials(QuoteHistoryEntry quote) => quote.Materials
        .Where(material => !string.IsNullOrWhiteSpace(material.Name))
        .OrderBy(material => material.SortOrder);

    public static string Format(QuoteHistorySummary order, QuoteHistoryEntry quote)
    {
        var names = new[] { order.CustomerName, order.ReferenceName }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim());
        string status = SupplierOrderQueue.StatusDisplay(order);
        var materials = GetMaterials(quote)
            .Select(material => $"- {material.Name.Trim()} — {material.QuantityDisplay}");
        string newLine = Environment.NewLine;

        return string.Join(newLine,
            string.Join(newLine, names.DefaultIfEmpty("Cliente non indicato")),
            "",
            $"Stato materiale: {(status == "-" ? "Da impostare" : status)}",
            "Materiali:",
            string.Join(newLine, materials.DefaultIfEmpty("Nessun materiale nel preventivo.")),
            "",
            "Note:",
            quote.CustomerNotes?.Trim() ?? "",
            "Note interne:",
            quote.Notes?.Trim() ?? "").TrimEnd();
    }
}
