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

    [Fact]
    public void SummaryUsesTheReferenceAddressWhenNoSiteIsSet()
    {
        var referenceId = Guid.NewGuid();
        var quote = new QuoteHistoryEntry
        {
            CustomerName = "Impresa Rossi", ReferenceName = "Condominio Verdi",
            ReferenceCustomerSyncId = referenceId, SiteName = " "
        };
        Customer[] customers =
        [
            new() { BusinessName = "Impresa Rossi", Address = "Via Cliente 1" },
            new() { BusinessName = "Verdi aggiornato", SyncId = referenceId, Address = " Via Riferimento 2 " }
        ];
        var intervention = new WorkScheduleEntry { SiteName = "Vecchio cantiere" };
        string expanded = SupplierOrderSummaryText.ExpandBlocks(WorkScheduleSettings.DefaultInterventionMessageTemplate);

        Assert.All(new[]
        {
            SupplierOrderSummaryText.Format(new QuoteHistorySummary(), quote, customers),
            SupplierOrderSummaryText.Format(intervention, quote, customers),
            SupplierOrderSummaryText.Format(intervention, quote, customers, expanded)
        }, message =>
        {
            Assert.Contains("Indirizzo riferimento: Via Riferimento 2", message);
            Assert.DoesNotContain("Via Cliente 1", message);
            Assert.DoesNotContain("Vecchio cantiere", message);
        });
    }

    [Theory]
    [InlineData("Via Cantiere 3", "Via Riferimento 2", "Indirizzo cantiere: Via Cantiere 3")]
    [InlineData("", "Via Riferimento 2", "Indirizzo riferimento: Via Riferimento 2")]
    [InlineData("", " ", "Indirizzo cliente: Via Cliente 1")]
    public void AutomaticAddressUsesSiteThenLegacyReferenceThenCustomer(string site, string referenceAddress, string expected)
    {
        var quote = new QuoteHistoryEntry
        {
            CustomerName = "Impresa Rossi", ReferenceName = "Condominio Verdi", SiteName = site
        };
        Customer[] customers =
        [
            new() { BusinessName = "Impresa Rossi", Address = "Via Cliente 1" },
            new() { BusinessName = " condominio verdi ", Address = referenceAddress }
        ];

        Assert.Equal(expected, SupplierOrderSummaryText.Format(new WorkScheduleEntry(), quote, customers,
            "{AddressLabel}: {Address}"));
        Assert.Contains(expected, SupplierOrderSummaryText.Format(new QuoteHistorySummary(), quote, customers));
    }

    [Fact]
    public void CalendarSummaryAddsTheAssignedCrewScheduleAndInterventionNotes()
    {
        var intervention = new WorkScheduleEntry
        {
            Date = new DateTime(2026, 11, 9), StartMinutes = 8 * 60, EndMinutes = 12 * 60 + 30,
            Employees = [new() { FirstName = "Anna", LastName = "Verdi", Abbreviation = "AV" }, new() { FirstName = "Luca" }],
            Notes = "Accesso dal retro"
        };
        var quote = new QuoteHistoryEntry
        {
            CustomerName = "Impresa Rossi", SiteName = "Via Roma 12", MaterialStatus = "Da ritirare",
            Materials = [new Item { Name = "Finestra", Quantity = 2 }],
            Notes = "Verificare la consegna"
        };

        Assert.Equal("""
            Cliente: Impresa Rossi
            Indirizzo cantiere: Via Roma 12
            Telefono cliente: 030 123456

            Intervento: lunedì 09/11/2026 · 08:00–12:30
            Squadra: Anna Verdi, Luca

            Stato materiale: Da ritirare
            Materiali:
            2 pz — Finestra

            Note interne:
            Verificare la consegna

            Note intervento:
            Accesso dal retro
            """.ReplaceLineEndings(), SupplierOrderSummaryText.Format(intervention, quote,
                [new Customer { BusinessName = "Impresa Rossi", Phone = "030 123456" }]));
    }

    [Fact]
    public void CalendarSummaryIncludesOrderedLaborsWithQuantitiesAndDescriptionsWithoutPrices()
    {
        var quote = new QuoteHistoryEntry
        {
            Labors =
            [
                new Item { Name = "Sigillatura", Quantity = 10000.5m, UnitOfMeasure = "m", UnitPrice = 12345, SortOrder = 2 },
                new Item { Name = "Posa finestra", Quantity = 2, Description = "Primo piano\nAccesso dal balcone", SortOrder = 1 },
                new Item { Name = " " }
            ]
        };

        string message = SupplierOrderSummaryText.Format(new WorkScheduleEntry(), quote, []);

        Assert.Contains("""
            Lavorazioni:
            2 pz — Posa finestra
              Primo piano
              Accesso dal balcone
            10000,5 m — Sigillatura
            """.ReplaceLineEndings(), message);
        Assert.DoesNotContain("12345", message);
    }

    [Fact]
    public void CustomTemplateControlsFieldOrderAndKeepsPlaceholderTextInsideNotesLiteral()
    {
        var entry = new WorkScheduleEntry
        {
            Employees = [new() { FirstName = "Anna", LastName = "Verdi" }], Notes = "Verificare {CustomerName}"
        };
        var quote = new QuoteHistoryEntry { QuoteNumber = "1234", CustomerName = "Impresa Rossi" };

        string message = SupplierOrderSummaryText.Format(entry, quote, [], """
            Ciao {Crew}
            Ordine {QuoteNumber} per {CustomerName}
            {LaborDetails}


            ATTENZIONE: {InterventionNotesText}
            {SegnapostoSconosciuto}
            """);

        Assert.Equal("""
            Ciao Anna Verdi
            Ordine 1234 per Impresa Rossi

            ATTENZIONE: Verificare {CustomerName}
            {SegnapostoSconosciuto}
            """.ReplaceLineEndings(), message);
    }

    [Fact]
    public void RepeatedRowsAllowCustomOrderAndOptionalDescriptionsWithoutExpandingInsertedText()
    {
        var quote = new QuoteHistoryEntry
        {
            CustomerName = "Impresa Rossi",
            Materials =
            [
                new Item { Name = "Profilo", Quantity = 10000.5m, UnitOfMeasure = "m", SortOrder = 2 },
                new Item { Name = "Finestra {CustomerName}", Quantity = 2, Description = "Piano terra", SortOrder = 1 }
            ],
            Labors = [new Item { Name = "Posa", Quantity = 2, Description = "Verificare {#Materials}" }]
        };

        string message = SupplierOrderSummaryText.Format(new WorkScheduleEntry(), quote, [], """
            Destinazione: {CustomerName}
            {#ReferenceName}Rif. {ReferenceName}{/ReferenceName}
            {#Materials}{RowNumber}) {Quantity} {Unit} | {Name}{#Description} ({Description}){/Description}
            {/Materials}
            {#Labors}- {Name}: {Description}
            {/Labors}
            """);

        Assert.DoesNotContain("Rif.", message);
        Assert.Contains("1) 2 pz | Finestra {CustomerName} (Piano terra)", message);
        Assert.Contains("2) 10000,5 m | Profilo", message);
        Assert.Contains("- Posa: Verificare {#Materials}", message);
    }

    [Fact]
    public void SeparateDateCrewAndContactFieldsAreAvailableInTheCustomMessage()
    {
        var customerId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        var quote = new QuoteHistoryEntry
        {
            CustomerName = "Cliente A", CustomerSyncId = customerId,
            ReferenceName = "Riferimento B", ReferenceCustomerSyncId = referenceId,
            SiteName = "Via Cantiere 1"
        };
        var entry = new WorkScheduleEntry
        {
            Date = new DateTime(2026, 11, 9), StartMinutes = 9 * 60, EndMinutes = 11 * 60 + 30,
            Employees = [new() { FirstName = "Anna", LastName = "Verdi", Abbreviation = "AV" }, new() { FirstName = "Luca" }]
        };
        Customer[] customers =
        [
            new() { SyncId = customerId, Address = "Via Cliente 2", Phone = "030 111111" },
            new() { SyncId = referenceId, Address = "Via Riferimento 3", Phone = "333 222222" }
        ];

        string message = SupplierOrderSummaryText.Format(entry, quote, customers, """
            {InterventionWeekday} {InterventionDateShort} dalle {StartTime} alle {EndTime}
            {SiteAddress} / {CustomerAddress} / {ReferenceAddress}
            {Phone} / {ReferencePhone}
            {CrewAbbreviations}
            {#CrewMembers}{RowNumber}. {FullName}{#Abbreviation} ({Abbreviation}){/Abbreviation}
            {/CrewMembers}
            """);

        Assert.Equal("""
            lunedì 09/11/2026 dalle 09:00 alle 11:30
            Via Cantiere 1 / Via Cliente 2 / Via Riferimento 3
            030 111111 / 333 222222
            AV, Luca
            1. Anna Verdi (AV)
            2. Luca
            """.ReplaceLineEndings(), message);
    }

    [Fact]
    public void ExpandingTheDefaultBlocksPreservesTheMessageAndMakesItsHeadingsEditable()
    {
        string expanded = SupplierOrderSummaryText.ExpandBlocks(WorkScheduleSettings.DefaultInterventionMessageTemplate);

        Assert.Contains("Cliente: {CustomerName}", expanded);
        Assert.Contains("{#Materials}", expanded);
        Assert.Contains("{#Labors}", expanded);
        Assert.Equal(SupplierOrderSummaryText.CreatePreview(null), SupplierOrderSummaryText.CreatePreview(expanded));
        Assert.Contains("Il tuo contatto:", SupplierOrderSummaryText.CreatePreview(expanded.Replace("Cliente:", "Il tuo contatto:")));

        var quote = new QuoteHistoryEntry { CustomerName = "Cliente A" };
        var entry = new WorkScheduleEntry();
        Assert.Equal(SupplierOrderSummaryText.Format(entry, quote, []), SupplierOrderSummaryText.Format(entry, quote, [], expanded));
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

    [Fact]
    public void AllAddressesFieldShowsOnlyAvailableAddressesWithTheirLabels()
    {
        string template = SupplierOrderSummaryText.TemplateFields
            .Single(field => field.Label == "Indirizzi · Tutti quelli disponibili").Text;
        Assert.Equal("""
            Indirizzo cantiere: Via Roma 12, Brescia
            Indirizzo riferimento: Via Dante 5, Brescia
            Indirizzo cliente: Via Milano 8, Brescia
            """.ReplaceLineEndings(), SupplierOrderSummaryText.CreatePreview(template));

        var quote = new QuoteHistoryEntry { CustomerName = "Impresa Rossi", ReferenceName = "Condominio Verdi" };
        Customer[] customers = [new() { BusinessName = "Impresa Rossi", Address = "Via Milano 8" }];
        Assert.Equal("Indirizzo cliente: Via Milano 8",
            SupplierOrderSummaryText.Format(new WorkScheduleEntry(), quote, customers, template));
    }
}
