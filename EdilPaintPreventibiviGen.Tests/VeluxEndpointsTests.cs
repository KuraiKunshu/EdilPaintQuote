using System.Net;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class VeluxEndpointsTests
{
    [Fact]
    public void LoginAndCatalogRequestsUseTheTemporaryOriginAndEscapeQueryInput()
    {
        Assert.Equal("https://appvelux.devdemo.it/cas/login", VeluxEndpoints.LoginUri.AbsoluteUri);
        Assert.Equal("https://appvelux.devdemo.it/preventivi/it/backoffice/", VeluxEndpoints.CatalogUri.AbsoluteUri);
        Assert.Equal("https://appvelux.devdemo.it/preventivi/it/backoffice/line_items/autocomplete_product_code?term=GGL%20%26%20CK%2F04%3F",
            VeluxEndpoints.SearchProductsUri("GGL & CK/04?").AbsoluteUri);
        Assert.Equal("https://appvelux.devdemo.it/preventivi/it/backoffice/product_data?id=id%26other%3Dvalue&description_type=false&description_change=false",
            VeluxEndpoints.ProductDetailsUri("id&other=value").AbsoluteUri);
    }

    [Fact]
    public void CookieRestoreIgnoresTheOldOriginAndPreservesCurrentCookieScopeAndFlags()
    {
        var container = new CookieContainer();

        VeluxSessionStorage.RestoreCookies(container, """
            {"cookies":[
              {"name":"old","value":"fixture","domain":"app.velux.it","path":"/"},
              {"name":"unrelated","value":"fixture","domain":"devdemo.it.example.org","path":"/"},
              {"name":"suffix","value":"fixture","domain":".it","path":"/"},
              {"name":"new","value":"fixture","domain":"appvelux.devdemo.it","path":"/preventivi","secure":true,"httpOnly":true}
            ]}
            """);

        var cookie = Assert.Single(container.GetCookies(VeluxEndpoints.CatalogUri).Cast<Cookie>());
        Assert.Equal("new", cookie.Name);
        Assert.Equal("/preventivi", cookie.Path);
        Assert.True(cookie.Secure);
        Assert.True(cookie.HttpOnly);
        Assert.Empty(container.GetCookies(VeluxEndpoints.LoginUri).Cast<Cookie>());
    }

    [Fact]
    public void ParentDomainAndLegacyCookiesAreRestoredWithoutReassigningTheirDomain()
    {
        var container = new CookieContainer();

        VeluxSessionStorage.RestoreCookies(container, """
            {"cookies":[{"name":"parent","value":"fixture","domain":".devdemo.it"}]}
            """);

        var cookie = Assert.Single(container.GetCookies(VeluxEndpoints.LoginUri).Cast<Cookie>());
        Assert.Equal(".devdemo.it", cookie.Domain);
        Assert.Equal("/", cookie.Path);
        Assert.False(cookie.Secure);
        Assert.False(cookie.HttpOnly);
    }

    [Fact]
    public void MalformedCookieDoesNotPreventSubsequentValidCookieFromLoading()
    {
        var container = new CookieContainer();

        VeluxSessionStorage.RestoreCookies(container, """
            {"cookies":[
              42,
              {"value":"fixture","domain":"appvelux.devdemo.it"},
              {"name":"","value":"fixture","domain":"appvelux.devdemo.it"},
              {"name":"bad-domain","value":"fixture","domain":123},
              {"name":"bad-path","value":"fixture","domain":"appvelux.devdemo.it","path":"relative"},
              {"name":"bad-flag","value":"fixture","domain":"appvelux.devdemo.it","secure":"true"},
              {"name":"valid","value":"fixture","domain":"appvelux.devdemo.it","path":"/"}
            ]}
            """);

        Assert.Equal("valid", Assert.Single(container.GetCookies(VeluxEndpoints.LoginUri).Cast<Cookie>()).Name);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"cookies\":{}}")]
    public void InvalidOrEmptySessionContentLeavesTheContainerEmpty(string json)
    {
        var container = new CookieContainer();

        VeluxSessionStorage.RestoreCookies(container, json);

        Assert.Empty(container.GetCookies(VeluxEndpoints.BaseUri).Cast<Cookie>());
    }
}
