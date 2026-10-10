using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using EdilPaintPreventibiviGen.Android.Models;
using EdilPaintPreventibiviGen.Android.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Android.Tests;

public sealed class MobileSupplierOrderEligibilityTests
{
    [Theory]
    [InlineData(QuoteStatus.Finito, false)]
    [InlineData(QuoteStatus.Finito, true)]
    [InlineData(QuoteStatus.Rifiutato, false)]
    [InlineData(QuoteStatus.Rifiutato, true)]
    [InlineData(QuoteStatus.Archiviato, false)]
    [InlineData(QuoteStatus.Archiviato, true)]
    public void ClosedQuotesAreExcludedRegardlessOfWhoOrderedMaterials(QuoteStatus status, bool orderedByCustomer)
        => AssertEligibility(status, orderedByCustomer, false, supplier: "MAFLAN SRL", materialStatus: "In magazzino");

    [Theory]
    [InlineData(QuoteStatus.Finalizzato)]
    [InlineData(QuoteStatus.Spedito)]
    [InlineData(QuoteStatus.Confermato)]
    public void OpenCustomerOrdersRemainVisible(QuoteStatus status)
        => AssertEligibility(status, true, true);

    [Fact]
    public void DeletedCustomerOrderIsExcluded()
        => AssertEligibility(QuoteStatus.Confermato, true, false, isDeleted: true);

    [Fact]
    public void ConfirmedQuoteNeedsAtLeastOneOrderField()
    {
        AssertEligibility(QuoteStatus.Confermato, false, false);
        AssertEligibility(QuoteStatus.Confermato, false, true, supplier: "Fornitore");
        AssertEligibility(QuoteStatus.Confermato, false, true, orderDate: new(2026, 10, 8));
        AssertEligibility(QuoteStatus.Confermato, false, true, deliveryDate: new(2026, 10, 9));
        AssertEligibility(QuoteStatus.Confermato, false, true, materialStatus: "Da ordinare");
    }

    [Theory]
    [InlineData(QuoteStatus.Finalizzato)]
    [InlineData(QuoteStatus.Spedito)]
    public void SupplierDetailsDoNotTurnUnconfirmedQuotesIntoOrders(QuoteStatus status)
        => AssertEligibility(status, false, false, supplier: "Fornitore", materialStatus: "Ordinato");

    private static void AssertEligibility(
        QuoteStatus status,
        bool orderedByCustomer,
        bool expected,
        bool isDeleted = false,
        string supplier = "",
        DateTime? orderDate = null,
        DateTime? deliveryDate = null,
        string materialStatus = "")
    {
        using var quotes = new DataTable { Locale = CultureInfo.InvariantCulture };
        quotes.Columns.Add("IsDeleted", typeof(bool));
        quotes.Columns.Add("Status", typeof(int));
        quotes.Columns.Add("MaterialsOrderedByCustomer", typeof(bool));
        quotes.Columns.Add("SupplierName", typeof(string));
        quotes.Columns.Add("MaterialOrderDate", typeof(DateTime));
        quotes.Columns.Add("ExpectedDeliveryDate", typeof(DateTime));
        quotes.Columns.Add("MaterialStatus", typeof(string));
        quotes.Rows.Add(isDeleted, (int)status, orderedByCustomer, supplier,
            (object?)orderDate ?? DBNull.Value, (object?)deliveryDate ?? DBNull.Value, materialStatus);

        // Evaluate the production SQL condition without opening the company database.
        // DataTable supports the boolean operators used here after adapting identifiers and parameters.
        foreach (string alias in new[] { "", "q" })
        {
            string filter = MobileDatabaseService.ActiveSupplierOrderFilter(alias);
            filter = Regex.Replace(filter, "(?:q\\.)?\"([A-Za-z]+)\"", "[$1]");
            filter = filter.Replace("@finished", ((int)QuoteStatus.Finito).ToString(CultureInfo.InvariantCulture))
                .Replace("@rejected", ((int)QuoteStatus.Rifiutato).ToString(CultureInfo.InvariantCulture))
                .Replace("@archived", ((int)QuoteStatus.Archiviato).ToString(CultureInfo.InvariantCulture))
                .Replace("@confirmed", ((int)QuoteStatus.Confermato).ToString(CultureInfo.InvariantCulture));
            Assert.Equal(expected, quotes.Select(filter).Length == 1);
        }
    }
}
