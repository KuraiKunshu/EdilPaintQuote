using EdilPaintPreventibiviGen.Models;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class InstallationCertificateRecipientTests
{
    [Fact]
    public void DefaultKeepsBothNamesAndReferenceOnlyUsesItsOwnAddress()
    {
        var quote = new QuoteHistoryEntry
        {
            CustomerName = "Cliente A",
            CustomerSyncId = Guid.NewGuid(),
            ReferenceName = "Riferimento B",
            ReferenceCustomerSyncId = Guid.NewGuid(),
            SiteName = "Via Cantiere 1"
        };
        Customer[] customers =
        [
            new() { SyncId = quote.CustomerSyncId, BusinessName = "Cliente A", Address = "Via Cliente 2" },
            new() { BusinessName = "Riferimento B", Address = "Via Omonimo 3" },
            new() { SyncId = quote.ReferenceCustomerSyncId, BusinessName = "Riferimento Rinominato", Address = " Via Riferimento 4 " }
        ];

        Assert.Collection(InstallationCertificateRecipient.CreateOptions(quote, customers),
            combined => AssertRecipient(combined, "Cliente A", "Via Cliente 2", "Riferimento B", "Via Riferimento 4"),
            client => AssertRecipient(client, "Cliente A", "Via Cliente 2", "", "Via Cliente 2"),
            reference => AssertRecipient(reference, "Riferimento B", "Via Riferimento 4", "", "Via Riferimento 4"));
    }

    [Fact]
    public void LegacyQuoteWithoutReferenceUsesItsUniqueCustomerAddress()
    {
        var quote = new QuoteHistoryEntry { CustomerName = " Cliente A " };
        Customer[] customers = [new() { BusinessName = " CLIENTE A ", Address = "Via Cliente 2" }];

        var recipient = Assert.Single(InstallationCertificateRecipient.CreateOptions(quote, customers));

        AssertRecipient(recipient, "Cliente A", "Via Cliente 2", "", "Via Cliente 2");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnmatchedStableIdOrAmbiguousLegacyNameLeavesTheAddressEmpty(bool unmatchedId)
    {
        var quote = new QuoteHistoryEntry
        {
            ReferenceName = "Riferimento B",
            ReferenceCustomerSyncId = unmatchedId ? Guid.NewGuid() : Guid.Empty
        };
        var customers = new List<Customer> { new() { BusinessName = "Riferimento B", Address = "Via Omonimo 3" } };
        if (!unmatchedId)
            customers.Add(new() { BusinessName = "Riferimento B", Address = "Via Altro Omonimo 4" });

        var recipient = Assert.Single(InstallationCertificateRecipient.CreateOptions(quote, customers));

        AssertRecipient(recipient, "Riferimento B", "", "", "");
    }

    private static void AssertRecipient(InstallationCertificateRecipient recipient,
        string name, string address, string referenceName, string workSiteFallback)
    {
        Assert.Equal(name, recipient.Name);
        Assert.Equal(address, recipient.Address);
        Assert.Equal(referenceName, recipient.ReferenceName);
        Assert.Equal(workSiteFallback, recipient.WorkSiteFallback);
    }
}
