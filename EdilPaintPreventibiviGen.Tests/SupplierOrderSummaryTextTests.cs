using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class SupplierOrderSummaryTextTests
{
    [Fact]
    public void FormatsTheTeamMessageWithSiteAddressAndQuantitiesBeforeMaterials()
    {
        var customerId = Guid.NewGuid();
        var order = new QuoteHistorySummary
        {
            CustomerName = " Impresa Rossi ", ReferenceName = "Condominio Verdi", MaterialStatus = "In magazzino"
        };
        var quote = new QuoteHistoryEntry
        {
            CustomerSyncId = customerId,
            SiteName = " Via Roma 12, Brescia ",
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
        Customer[] customers =
        [
            new() { BusinessName = "Impresa Rossi", Phone = "030 000000" },
            new() { SyncId = customerId, BusinessName = "Rossi SRL", Address = "Via Verdi 5", Phone = " 333 1234567 " }
        ];

        Assert.Equal("""
            Cliente: Impresa Rossi
            Riferimento: Condominio Verdi
            Indirizzo cantiere: Via Roma 12, Brescia
            Telefono cliente: 333 1234567

            Stato materiale: In magazzino
            Materiali:
            2 pz — Finestra
            10000,5 m — Scossalina

            Note:
            Consegna in cantiere

            Note interne:
            Chiamare il caposquadra
            """.ReplaceLineEndings(), SupplierOrderSummaryText.Format(order, quote, customers));
    }

    [Fact]
    public void UsesTheLegacyCustomerAddressAndPhoneWhenThereIsNoSiteAddress()
    {
        var order = new QuoteHistorySummary { CustomerName = "Impresa Rossi" };
        var quote = new QuoteHistoryEntry { SiteName = " " };
        Customer[] customers =
        [
            new() { BusinessName = " IMPRESA ROSSI ", Address = " Via Verdi 5 ", Phone = "030 123456" }
        ];

        Assert.Equal("""
            Cliente: Impresa Rossi
            Indirizzo cliente: Via Verdi 5
            Telefono cliente: 030 123456

            Stato materiale: Da impostare
            Materiali:
            Nessun materiale nel preventivo.
            """.ReplaceLineEndings(), SupplierOrderSummaryText.Format(order, quote, customers));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OmitsContactDetailsWhenTheCustomerIdentityIsUncertain(bool hasUnmatchedId)
    {
        var order = new QuoteHistorySummary { CustomerName = "Impresa Rossi" };
        var quote = new QuoteHistoryEntry { CustomerSyncId = hasUnmatchedId ? Guid.NewGuid() : Guid.Empty };
        var customers = new List<Customer> { new() { BusinessName = "Impresa Rossi", Address = "Via Roma 12" } };
        if (!hasUnmatchedId)
            customers.Add(new() { BusinessName = "Impresa Rossi", Phone = "030 123456" });

        string message = SupplierOrderSummaryText.Format(order, quote, customers);

        Assert.DoesNotContain("Indirizzo cliente:", message);
        Assert.DoesNotContain("Telefono cliente:", message);
    }
}
