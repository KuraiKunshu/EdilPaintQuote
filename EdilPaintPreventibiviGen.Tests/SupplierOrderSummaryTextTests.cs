using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class SupplierOrderSummaryTextTests
{
    [Fact]
    public void FormatsTheVisibleStatusAndKeepsCustomerNotesSeparateFromInternalNotes()
    {
        var order = new QuoteHistorySummary
        {
            CustomerName = " Impresa Rossi ", ReferenceName = "Condominio Verdi", MaterialStatus = "In magazzino"
        };
        var quote = new QuoteHistoryEntry
        {
            MaterialStatus = "Ordinato",
            CustomerNotes = "Consegna in cantiere",
            Notes = "Chiamare il caposquadra",
            Materials =
            [
                new Item { Name = "Scossalina", Quantity = 10000.5m, UnitOfMeasure = "m", SortOrder = 2 },
                new Item { Name = "Finestra", Quantity = 2m, SortOrder = 1 },
                new Item { Name = " " }
            ],
            Labors = [new Item { Name = "Posa" }]
        };

        Assert.Equal("""
            Impresa Rossi
            Condominio Verdi

            Stato materiale: In magazzino
            Materiali:
            - Finestra — 2 pz
            - Scossalina — 10000,5 m

            Note:
            Consegna in cantiere
            Note interne:
            Chiamare il caposquadra
            """.ReplaceLineEndings(), SupplierOrderSummaryText.Format(order, quote));
    }

    [Fact]
    public void HandlesAnOrderWithoutAReferenceStatusOrMaterials()
    {
        var order = new QuoteHistorySummary { CustomerName = "Impresa Rossi" };

        Assert.Equal("""
            Impresa Rossi

            Stato materiale: Da impostare
            Materiali:
            Nessun materiale nel preventivo.

            Note:

            Note interne:
            """.ReplaceLineEndings(), SupplierOrderSummaryText.Format(order, new QuoteHistoryEntry()));
    }
}
