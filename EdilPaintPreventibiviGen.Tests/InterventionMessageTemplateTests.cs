using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class InterventionMessageTemplateTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> NoRows =
        new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>();

    [Fact]
    public void ReplacesFieldsWithoutExecutingBracesInDataAndPreservesExactWhitespace()
    {
        var fields = new Dictionary<string, string> { ["CustomerName"] = "Cliente A", ["Notes"] = "Verifica {CustomerName} e {#Labors}test{/Labors}" };

        string result = InterventionMessageTemplate.Render("  {customername}\n\n\n{Notes}\n{Unknown}  ", fields, NoRows);

        Assert.Equal("  Cliente A\n\n\nVerifica {CustomerName} e {#Labors}test{/Labors}\n{Unknown}  ", result);
    }

    [Fact]
    public void NestedOptionalBlocksUseNonblankScalarValuesAndMatchingNamesIgnoringCase()
    {
        var fields = new Dictionary<string, string> { ["Phone"] = "030 123456", ["ReferenceName"] = " B ", ["Notes"] = "  " };

        string result = InterventionMessageTemplate.Render(
            "{#phone}Telefono {Phone}{#ReferenceName} per {ReferenceName}{/referencename}{/PHONE}{#Notes}Note vuote{/Notes}{#Missing}Mancante{/Missing}",
            fields, NoRows);

        Assert.Equal("Telefono 030 123456 per  B ", result);
    }

    [Fact]
    public void RepeatedRowsOverrideGlobalFieldsAndCanContainOptionalDescriptions()
    {
        var fields = new Dictionary<string, string> { ["CustomerName"] = "Cliente A", ["Name"] = "Nome globale", ["Materials"] = "Lista predefinita" };
        IReadOnlyList<IReadOnlyDictionary<string, string>> materials =
        [
            new Dictionary<string, string> { ["Name"] = "Finestra", ["Quantity"] = "2", ["Description"] = "{Name}" },
            new Dictionary<string, string> { ["Name"] = "Raccordo", ["Quantity"] = "1", ["Description"] = "" }
        ];
        var rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["Materials"] = materials };

        string result = InterventionMessageTemplate.Render(
            "{Materials}\n{#materials}{quantity} {name} / {CustomerName}{#Description}: {Description}{/Description}\n{/Materials}", fields, rows);

        Assert.Equal("Lista predefinita\n2 Finestra / Cliente A: {Name}\n1 Raccordo / Cliente A\n", result);
    }

    [Fact]
    public void EmptyCollectionsDoNotRenderEvenWhenTheLegacyListFieldHasText()
    {
        var fields = new Dictionary<string, string> { ["Materials"] = "Nessun materiale" };
        var rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["Materials"] = [] };

        Assert.Equal("Nessun materiale", InterventionMessageTemplate.Render(
            "{#Materials}Materiale {Name}{/Materials}{Materials}", fields, rows));
    }

    [Theory]
    [InlineData("{#Notes}Senza chiusura")]
    [InlineData("{/Notes}")]
    [InlineData("{#Notes}Test{/Phone}")]
    [InlineData("{#Notes")]
    [InlineData("{#}")]
    public void MalformedBlockTagsAreRejectedBeforeAnyConditionalContentIsEvaluated(string template)
        => Assert.Throws<InvalidOperationException>(() => InterventionMessageTemplate.Render(template,
            new Dictionary<string, string>(), NoRows));

    [Fact]
    public void NestingIsBoundedAtSixteenBlocks()
    {
        var fields = new Dictionary<string, string> { ["Notes"] = "Presente" };
        string valid = string.Concat(Enumerable.Repeat("{#Notes}", 16)) + "ok" + string.Concat(Enumerable.Repeat("{/Notes}", 16));
        Assert.Equal("ok", InterventionMessageTemplate.Render(valid, fields, NoRows));
        Assert.Throws<InvalidOperationException>(() => InterventionMessageTemplate.Render("{#Notes}" + valid + "{/Notes}", fields, NoRows));
    }

    [Fact]
    public void CollectionsCannotBeRepeatedInsideAnotherCollection()
    {
        var rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
        {
            ["Materials"] = [new Dictionary<string, string> { ["Name"] = "Finestra" }],
            ["Labors"] = [new Dictionary<string, string> { ["Name"] = "Posa" }]
        };

        Assert.Throws<InvalidOperationException>(() => InterventionMessageTemplate.Render(
            "{#Materials}{#Labors}{Name}{/Labors}{/Materials}", new Dictionary<string, string>(), rows));
    }

    [Theory]
    [InlineData("{#AbsentScalar}{#Materials}{#Labors}{Name}{/Labors}{/Materials}{/AbsentScalar}")]
    [InlineData("{#Materials}{#Labors}{Name}{/Labors}{/Materials}")]
    public void NestedCollectionsAreRejectedEvenWhenTheCurrentDataWouldSkipTheirRendering(string template)
    {
        var rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
        {
            ["Materials"] = [],
            ["Labors"] = []
        };

        Assert.Throws<InvalidOperationException>(() => InterventionMessageTemplate.Render(
            template, new Dictionary<string, string>(), rows));
    }
}
