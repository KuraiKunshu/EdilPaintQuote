using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class MaterialCatalogSearchTests
{
    [Fact]
    public void SearchFindsShortNamesAndDescriptionsAndExcludesInternalCosts()
    {
        var material = new Item { PersistentId = 12, Name = "A", Description = "Vernice lavabile", UnitOfMeasure = "l", UnitPrice = 15 };
        var internalCost = new Item { Name = "A", IsCompanyMaterial = true };
        Assert.Same(material, Assert.Single(MaterialCatalogSearch.Find([material, internalCost], "a")).Item);
        var result = Assert.Single(MaterialCatalogSearch.Find([material], " LAVABILE "));
        Assert.Same(material, result.Item);
        Assert.Equal("A", result.Value);
        Assert.Contains("€/l", result.Label);
    }

    [Fact]
    public void SameNameMaterialsKeepTheirOwnCatalogIdentity()
    {
        var first = new Item { PersistentId = 1, Name = "Pannello", UnitOfMeasure = "m²" };
        var second = new Item { PersistentId = 2, Name = "Pannello", UnitOfMeasure = "pz" };
        var results = MaterialCatalogSearch.Find([first, second], "Pannello");
        Assert.Collection(results, x => Assert.Same(first, x.Item), x => Assert.Same(second, x.Item));
    }

    [Fact]
    public void EmptySearchAndCancellationDoNotReturnStaleResults()
    {
        var catalog = new[] { new Item { Name = "Materiale" } };
        Assert.Empty(MaterialCatalogSearch.Find(catalog, " "));
        Assert.Empty(MaterialCatalogSearch.Find(catalog, "Non presente"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => MaterialCatalogSearch.Find(catalog, "Materiale", cancellation.Token));
    }
}
