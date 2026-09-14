using System.Text.Json;
using EdilPaintPreventibiviGen.Models;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class InstallationCertificateSelectionTests
{
    [Fact]
    public void CertificateRequiresAnExplicitNonEmptySelection()
    {
        var selection = new InstallationCertificateSelection([new Item { Name = "Finestra", Quantity = 2 }]);

        Assert.All(selection.Materials, option => Assert.False(option.IsSelected));
        Assert.Throws<InvalidOperationException>(() => selection.GetSelectedMaterials());
        Assert.Throws<InvalidOperationException>(() => new InstallationCertificateSelection([]).GetSelectedMaterials());
    }

    [Fact]
    public void SelectionUsesIndividualQuoteLinesAndPreservesOrderAndOriginalData()
    {
        var excluded = new Item { PersistentId = 7, Name = "Finestra", Description = "Piano terra", Quantity = 3, SortOrder = 1 };
        var included = new Item { PersistentId = 7, Name = "Finestra", Description = "Primo piano", Quantity = 2, SortOrder = 2 };
        var first = new Item { Name = "Raccordo", Quantity = 4, SortOrder = 0 };
        Item[] original = [included, excluded, first];
        string before = JsonSerializer.Serialize(original);
        var selection = new InstallationCertificateSelection(original);

        selection.Materials[2].IsSelected = true;
        selection.Materials[0].IsSelected = true;

        Assert.Equal(new[] { first, included }, selection.GetSelectedMaterials());
        Assert.DoesNotContain(excluded, selection.GetSelectedMaterials());
        Assert.Equal(before, JsonSerializer.Serialize(original));
    }

    [Fact]
    public void BulkSelectionOmitsInvalidLinesAndSupportsDeselectingOneOrAllMaterials()
    {
        var first = new Item { Name = "Finestra", Quantity = 2 };
        var second = new Item { Name = "Raccordo", Quantity = 1 };
        var selection = new InstallationCertificateSelection([
            first, new Item { Name = "  " }, new Item { Name = "Quantita zero", Quantity = 0 },
            second, new Item { Name = "Reso", Quantity = -1 }
        ]);

        selection.SelectAll(true);
        Assert.Equal(new[] { first, second }, selection.GetSelectedMaterials());
        selection.Materials[1].IsSelected = false;
        Assert.Same(first, Assert.Single(selection.GetSelectedMaterials()));
        selection.SelectAll(false);
        Assert.Throws<InvalidOperationException>(() => selection.GetSelectedMaterials());
        selection.Materials[1].IsSelected = true;
        Assert.Same(second, Assert.Single(selection.GetSelectedMaterials()));
    }
}
