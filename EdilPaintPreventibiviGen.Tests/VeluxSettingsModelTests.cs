using System.Text.Json;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class VeluxSettingsModelTests
{
    [Fact]
    public void DefaultsResolveTheTemporarySiteAndEncodeRuntimeQueryValues()
    {
        var settings = new VeluxSettingsModel().CreateValidatedCopy();

        Assert.Equal("https://appvelux.devdemo.it/cas/login", settings.LoginUri.AbsoluteUri);
        Assert.Equal("https://appvelux.devdemo.it/preventivi/it/backoffice/line_items/autocomplete_product_code?term=GGU%20%26%20MK04%3F",
            settings.SearchProductsUri("GGU & MK04?").AbsoluteUri);
        Assert.Contains("id=id%26other%3Dvalue&description_type=false", settings.ProductDetailsUri("id&other=value").AbsoluteUri);
        Assert.Equal(8, settings.RequestTimeoutSeconds);
    }

    [Fact]
    public void DomainAndCatalogChangesApplyToRelativeRequestTemplates()
    {
        var settings = new VeluxSettingsModel
        {
            BaseUrl = " APP.VELUX.IT ", CatalogPath = "/nuovo/catalogo", RequestTimeoutSeconds = 45
        }.CreateValidatedCopy();

        Assert.Equal("https://app.velux.it/", settings.BaseUrl);
        Assert.Equal("/nuovo/catalogo/", settings.CatalogPath);
        Assert.Equal("https://app.velux.it/nuovo/catalogo/line_items/autocomplete_product_code?term=test",
            settings.SearchProductsUri("test").AbsoluteUri);
        Assert.Equal("https://app.velux.it/nuovo/catalogo/product_data?id=abc&description_type=false&description_change=false",
            settings.ProductDetailsUri("abc").AbsoluteUri);
        Assert.Equal(settings, settings.CreateValidatedCopy());
    }

    [Fact]
    public void AbsoluteSameOriginEndpointsNormalizeToPortableOriginRelativePaths()
    {
        var settings = new VeluxSettingsModel
        {
            BaseUrl = "https://velux.example:8443/",
            LoginPath = "https://velux.example:8443/login?service=preventivi",
            CatalogPath = "https://velux.example:8443/catalogo",
            SearchPath = "https://velux.example:8443/search?q={term}",
            ProductDetailsPath = "/details/{id}"
        }.CreateValidatedCopy();

        Assert.Equal("/login?service=preventivi", settings.LoginPath);
        Assert.Equal("/catalogo/", settings.CatalogPath);
        Assert.Equal("/search?q={term}", settings.SearchPath);
        Assert.Equal("https://velux.example:8443/search?q=a%26b", settings.SearchProductsUri("a&b").AbsoluteUri);
        Assert.Equal("https://velux.example:8443/details/a%2Fb", settings.ProductDetailsUri("a/b").AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://appvelux.devdemo.it")]
    [InlineData("https://user:password@appvelux.devdemo.it")]
    [InlineData("https://appvelux.devdemo.it/cas/login")]
    [InlineData("https://appvelux.devdemo.it/?service=preventivi")]
    [InlineData("https://appvelux.devdemo.it/#login")]
    public void BaseAddressMustBeAnHttpsOrigin(string baseUrl)
        => Assert.Throws<InvalidOperationException>(() => new VeluxSettingsModel { BaseUrl = baseUrl }.CreateValidatedCopy());

    [Fact]
    public void EndpointOriginsFragmentsAndRequiredQueryPlaceholdersAreValidated()
    {
        Assert.Throws<InvalidOperationException>(() => new VeluxSettingsModel { LoginPath = "https://other.example/login" }.CreateValidatedCopy());
        Assert.Throws<InvalidOperationException>(() => new VeluxSettingsModel { CatalogPath = "/catalogo?source=test" }.CreateValidatedCopy());
        Assert.Throws<InvalidOperationException>(() => new VeluxSettingsModel { SearchPath = "search?term=value" }.CreateValidatedCopy());
        Assert.Throws<InvalidOperationException>(() => new VeluxSettingsModel { ProductDetailsPath = "product/{id}#result" }.CreateValidatedCopy());
        Assert.Throws<InvalidOperationException>(() => new VeluxSettingsModel { SearchPath = "https://appvelux.devdemo.it:8443/search?term={term}" }.CreateValidatedCopy());
        Assert.Throws<InvalidOperationException>(() => new VeluxSettingsModel { RequestTimeoutSeconds = 0 }.CreateValidatedCopy());
        Assert.Throws<InvalidOperationException>(() => new VeluxSettingsModel { RequestTimeoutSeconds = 121 }.CreateValidatedCopy());
    }

    [Fact]
    public void SettingsRoundTripPreservesExistingConfigurationAndStoresOnlyEditableVeluxValues()
    {
        string directory = Path.Combine(Path.GetTempPath(), "velux-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "appsettings.json");
        try
        {
            File.WriteAllText(path, """{"OtherSection":{"Keep":"yes"}}""");
            var settings = new AppSettingsService(new ConfigurationBuilder().Build(), path);
            settings.Velux.BaseUrl = "app.velux.it";
            settings.Velux.CatalogPath = "/nuovo/";
            settings.Velux.RequestTimeoutSeconds = 42;
            settings.Save();

            using var saved = JsonDocument.Parse(File.ReadAllText(path));
            var velux = saved.RootElement.GetProperty("Velux");
            Assert.Equal("yes", saved.RootElement.GetProperty("OtherSection").GetProperty("Keep").GetString());
            Assert.Equal("https://app.velux.it/", velux.GetProperty("BaseUrl").GetString());
            Assert.False(velux.TryGetProperty("BaseUri", out _));
            Assert.False(velux.TryGetProperty("LoginUri", out _));
            var reloaded = new AppSettingsService(new ConfigurationBuilder().AddJsonFile(path).Build(), path);
            Assert.Equal(settings.Velux.CreateValidatedCopy(), reloaded.Velux.CreateValidatedCopy());
            string beforeInvalidSave = File.ReadAllText(path);
            settings.Velux.BaseUrl = "http://invalid.example";
            Assert.Throws<InvalidOperationException>(settings.Save);
            Assert.Equal(beforeInvalidSave, File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void MissingOrInvalidManualConfigurationDoesNotBlockApplicationStartup()
    {
        string unusedPath = Path.Combine(Path.GetTempPath(), "velux-unused-" + Guid.NewGuid().ToString("N") + ".json");
        var missing = new AppSettingsService(new ConfigurationBuilder().Build(), unusedPath);
        Assert.Equal(new VeluxSettingsModel(), missing.Velux);
        var badUrl = new AppSettingsService(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Velux:BaseUrl"] = "http://invalid.example" }).Build(), unusedPath);
        Assert.Equal("http://invalid.example", badUrl.Velux.BaseUrl);
        Assert.Throws<InvalidOperationException>(() => badUrl.Velux.CreateValidatedCopy());
        var badTimeout = new AppSettingsService(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Velux:RequestTimeoutSeconds"] = "invalid" }).Build(), unusedPath);
        Assert.Equal(new VeluxSettingsModel(), badTimeout.Velux);
        Assert.False(File.Exists(unusedPath));
    }
}
