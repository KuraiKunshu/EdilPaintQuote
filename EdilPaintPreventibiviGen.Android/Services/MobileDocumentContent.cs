using System.Globalization;
using EdilPaintPreventibiviGen.Android.Models;

namespace EdilPaintPreventibiviGen.Android.Services;

public sealed record DocumentLine(string Text, bool Heading = false);

public static class MobileDocumentContent
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");
    private static string Money(double value) => value.ToString("C", Italian);

    public static IReadOnlyList<DocumentLine> Quote(QuoteDetail quote, CompanyContact company)
    {
        var lines = new List<DocumentLine>
        {
            new(company.Name, true), new(company.Address), new($"P. IVA {company.VatNumber}   {company.Email}"),
            new($"PREVENTIVO {quote.QuoteNumber}", true), new($"Data: {quote.Date:dd/MM/yyyy}"),
            new(quote.CustomerName, true), new(quote.CustomerAddress)
        };
        if (!string.IsNullOrWhiteSpace(quote.ReferenceName)) lines.Add(new($"Riferimento: {quote.ReferenceName}"));
        if (!string.IsNullOrWhiteSpace(quote.SiteName)) lines.Add(new($"Cantiere: {quote.SiteName}"));
        if (!string.IsNullOrWhiteSpace(quote.BillingCustomerName)) lines.Add(new($"Intestatario: {quote.BillingCustomerName}"));
        AddLines(lines, "Materiali", quote.Materials);
        AddLines(lines, "Lavorazioni", quote.Labors);
        lines.Add(new($"Sconto materiali: {quote.MaterialDiscount:0.##}%   Sconto lavorazioni: {quote.LaborDiscount:0.##}%"));
        lines.Add(new($"Imponibile: {Money(quote.Imponibile)}"));
        lines.Add(new($"IVA {quote.IvaDisplay}: {Money(quote.Total - quote.Imponibile)}"));
        lines.Add(new($"TOTALE: {Money(quote.Total)}", true));
        if (!string.IsNullOrWhiteSpace(quote.PaymentTerms))
        {
            lines.Add(new("TERMINI DI PAGAMENTO", true));
            lines.Add(new(quote.PaymentTerms));
        }
        if (!string.IsNullOrWhiteSpace(quote.CustomerNotes))
        {
            lines.Add(new("NOTE", true));
            lines.Add(new(quote.CustomerNotes));
        }
        lines.Add(new("Firma per accettazione: __________________________"));
        return lines;
    }

    private static void AddLines(List<DocumentLine> output, string title, IEnumerable<QuoteLine> lines)
    {
        output.Add(new(title.ToUpperInvariant(), true));
        foreach (var line in lines.OrderBy(line => line.SortOrder))
        {
            output.Add(new($"N.{line.Quantity}  {line.Name}", true));
            if (!string.IsNullOrWhiteSpace(line.Description)) output.Add(new(line.Description));
            output.Add(new($"Prezzo unit. {Money(line.UnitPrice)}   Sconto {line.Discount:0.##}%   Totale {Money(line.Total)}"));
        }
    }

    public static IReadOnlyList<DocumentLine> RealProfit(QuoteDetail quote, RealProfitInput input)
    {
        RealProfitResult result = RealProfitCalculator.Calculate(input);
        var lines = new List<DocumentLine>
        {
            new("GUADAGNO REALE - RISERVATO", true), new($"Preventivo {quote.QuoteNumber} - {quote.CustomerName}"),
            new($"Ricavo: {Money(input.QuoteRevenue)}"), new($"Materiali esclusi: {(input.ExcludeMaterials ? "Si" : "No")}"),
            new($"Sconto fornitore: {input.SupplierDiscount:0.##}%"),
            new($"Operai {input.Workers} x giorni {input.Days} x ore {input.HoursPerDay} x {Money(input.HourlyCost)}"),
            new("COSTI AZIENDALI", true)
        };
        foreach (var cost in input.CompanyMaterials)
            lines.Add(new($"N.{cost.Quantity} {cost.Name} - {Money(cost.UnitCost)} / cad. - {Money(cost.Total)}"));
        lines.AddRange(new[]
        {
            new DocumentLine($"Costo fornitore: {Money(result.SupplierMaterialCost)}"),
            new DocumentLine($"Manodopera: {Money(result.LaborCost)}"),
            new DocumentLine($"Costi totali: {Money(result.TotalCosts)}"),
            new DocumentLine($"Riduzione prudenziale {input.ProfitReductionPercentage:0.##}%: {Money(result.ProfitReductionAmount)}"),
            new DocumentLine($"GUADAGNO: {Money(result.Profit)} ({result.ProfitPercentage:0.##}%)", true)
        });
        return lines;
    }

    public static IReadOnlyList<DocumentLine> Collaboration(QuoteDetail quote)
    {
        var lines = new List<DocumentLine> { new("COSTI COLLABORAZIONE - RISERVATO", true), new($"Preventivo {quote.QuoteNumber} - {quote.PartnerCompanyName}") };
        foreach (var (title, costs) in new[] { ("Nostri costi", quote.OurCosts), ("Costi partner", quote.PartnerCosts), ("Costi aggiuntivi", quote.AdditionalCosts) })
        {
            lines.Add(new(title, true));
            foreach (var cost in costs) lines.Add(new($"{cost.Description}: {Money(cost.Amount)}\n{cost.Notes}"));
            lines.Add(new($"Totale: {Money(costs.Sum(cost => cost.Amount))}"));
        }
        return lines;
    }

    public static IReadOnlyList<DocumentLine> InstallationCertificate(QuoteDetail quote, CompanyContact company, string site, DateTime completed)
    {
        var lines = new List<DocumentLine>
        {
            new(company.Name, true), new(company.Address), new($"P. IVA {company.VatNumber} - {company.Email}"),
            new("CERTIFICATO DI POSA IN OPERA", true), new($"Preventivo {quote.QuoteNumber} - {quote.CustomerName}"),
            new($"Cantiere: {site}"), new($"Data completamento: {completed:dd/MM/yyyy}"),
            new("DICHIARA E CERTIFICA", true),
            new($"La sottoscritta impresa {company.Name}, con riferimento ai lavori eseguiti presso il cantiere sopra indicato, certifica che i materiali elencati nel presente documento sono stati posati in opera a regola d'arte, nel rispetto delle indicazioni dei produttori e delle norme tecniche applicabili."),
            new("Per mantenere la buona funzionalita dei prodotti, si consiglia di eseguire la manutenzione periodica, come indicato sulle istruzioni d'uso e manutenzione del prodotto."),
            new("MATERIALI POSATI", true)
        };
        foreach (var material in quote.Materials.OrderBy(x => x.SortOrder))
        {
            lines.Add(new($"N.{material.Quantity} {material.Name}", true));
            if (!string.IsNullOrWhiteSpace(material.Description)) lines.Add(new(material.Description));
        }
        lines.Add(new("Timbro e firma dell'impresa: __________________________"));
        return lines;
    }
}
