using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class VeluxProductDetailsTests
{
    [Theory]
    [InlineData("<html><form id='login-form'>Login VELUX</form></html>")]
    [InlineData("<html><h1>Service unavailable</h1></html>")]
    [InlineData("")]
    public void LoginAndErrorPagesDoNotCreateAnUnpricedProduct(string response)
        => Assert.Null(VeluxService.ParseProductDetails(response));

    [Fact]
    public void ValidProductResponseRetainsNameDescriptionAndPrice()
    {
        var item = VeluxService.ParseProductDetails("""
            $('#line_item_name').val('GGU MK04');
            $('#line_item_description').val('Finestra per tetti');
            $('#line_item_price_cents').val('123456');
            """);

        Assert.NotNull(item);
        Assert.Equal("GGU MK04", item.Name);
        Assert.Equal("Finestra per tetti", item.Description);
        Assert.Equal(1234.56, item.UnitPrice, 2);
        Assert.Equal(1, item.Quantity);
    }
}
