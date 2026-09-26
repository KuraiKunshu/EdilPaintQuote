using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class CompanyInstallationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "company-tests-" + Guid.NewGuid().ToString("N"));
    public CompanyInstallationTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void CleanDefaultsHaveNoCredentialsEmployeesOrWindowRules()
    {
        string path = Path.Combine(_root, "appsettings.json");
        AppSettingsFileService.WriteDefaultSettings(path, generic: true);
        var config = new ConfigurationBuilder().AddJsonFile(path).Build();
        var business = config.GetSection("Business").Get<BusinessSettingsModel>()!;
        var app = config.GetSection("App").Get<AppSettingsServiceModel>()!;
        var profit = config.GetSection("RealProfit").Get<RealProfitSettingsModel>()!;
        profit.Normalize();
        Assert.False(business.EnableWindowAutomations);
        Assert.False(business.EnableInstallationCertificate);
        Assert.False(business.UseLegacyStamp);
        Assert.Empty(business.StampFileName);
        Assert.False(app.UseVeluxLogin);
        Assert.False(app.ImportLegacyData);
        Assert.False(app.FirstStartup);
        Assert.Equal("22%", app.GetEffectiveDefaultVatType());
        Assert.Empty(profit.WindowMaterialRules);
        Assert.Empty(config.GetSection("Employees").GetChildren());
        foreach (string field in new[] { "Server", "DatabaseName", "Username", "Password" })
            Assert.Equal(string.Empty, config["Database:" + field]);
        Assert.Equal(string.Empty, config["Mail:Password"]);
        Assert.Equal(string.Empty, config["Mail:SenderEmail"]);
        Assert.DoesNotContain("EdilPaint", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OldSettingsKeepTheirFeaturesAndVat()
    {
        var app = new AppSettingsServiceModel();
        var business = new BusinessSettingsModel();
        Assert.True(app.ImportLegacyData);
        Assert.True(business.EnableWindowAutomations);
        Assert.True(business.EnableInstallationCertificate);
        Assert.True(business.UseLegacyStamp);
        Assert.Equal("RC 10%+22%", app.GetEffectiveDefaultVatType());
    }

    [Fact]
    public void ProfilesAreSeparateAndLegacyLocationDoesNotChange()
    {
        string one = Guid.NewGuid().ToString();
        string two = Guid.NewGuid().ToString();
        string old = CompanyInstallationService.GetRootDirectory(_root, null);
        Assert.Equal(Path.Combine(_root, "EdilPaintPreventivi"), old);
        Assert.NotEqual(old, CompanyInstallationService.GetRootDirectory(_root, one));
        Assert.NotEqual(CompanyInstallationService.GetRootDirectory(_root, one), CompanyInstallationService.GetRootDirectory(_root, two));
        Assert.Equal(CompanyInstallationService.GetRootDirectory(_root, one), CompanyInstallationService.GetRootDirectory(_root, Guid.Parse(one).ToString("N")));
    }

    [Fact]
    public void CompanyBuildRequiresMarkerAndInvalidMarkerNeverFallsBack()
    {
        Assert.Null(CompanyInstallationService.ReadProfileId(_root));
        Assert.Throws<InvalidOperationException>(() => CompanyInstallationService.ReadProfileId(_root, requireMarker: true));
        string marker = Path.Combine(_root, CompanyInstallationService.MarkerFileName);
        File.WriteAllText(marker, "not-a-company-id");
        Assert.Throws<InvalidOperationException>(() => CompanyInstallationService.ReadProfileId(_root));
        var id = Guid.NewGuid();
        File.WriteAllText(marker, id.ToString());
        Assert.Equal(id.ToString("N"), CompanyInstallationService.ReadProfileId(_root, true));
    }

    [Fact]
    public void CleanArchiveNeverImportsLegacySeedAndLegacyImportDoesNotOverwriteExistingFiles()
    {
        string assets = Path.Combine(_root, "Assets");
        string clean = Path.Combine(_root, "Clean");
        string legacy = Path.Combine(_root, "Legacy");
        Directory.CreateDirectory(assets);
        File.WriteAllText(Path.Combine(assets, "azienda.json"), "legacy-company");
        File.WriteAllText(Path.Combine(assets, "history.json"), "legacy-history");
        LocalApplicationDataService.InitializeDataDirectory(assets, clean, false);
        Assert.Empty(Directory.GetFiles(clean));
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "azienda.json"), "current-company");
        LocalApplicationDataService.InitializeDataDirectory(assets, legacy, true);
        Assert.Equal("current-company", File.ReadAllText(Path.Combine(legacy, "azienda.json")));
        Assert.Equal("legacy-history", File.ReadAllText(Path.Combine(legacy, "history.json")));
    }

    [Fact]
    public void CompanyLogoDoesNotUseLegacyAssetsAndRemovingLogoStaysEmpty()
    {
        string assets = Path.Combine(_root, "Assets");
        string branding = Path.Combine(_root, "Branding");
        Directory.CreateDirectory(assets);
        Directory.CreateDirectory(branding);
        string legacyLogo = Path.Combine(assets, "logo.png");
        File.WriteAllText(legacyLogo, "legacy");
        var company = new Company { Logo = [legacyLogo] };
        Assert.Empty(CompanyBrandingService.ResolveLogo(company, "logo.png", branding, assets, true));
        Assert.Equal(legacyLogo, CompanyBrandingService.ResolveLogo(company, "logo.png", branding, assets, false));
        string ownLogo = Path.Combine(branding, "logo.png");
        File.WriteAllText(ownLogo, "company");
        Assert.Equal(ownLogo, CompanyBrandingService.ResolveLogo(company, "logo.png", branding, assets, true));
        Assert.Empty(CompanyBrandingService.ResolveLogo(company, "", branding, assets, false));
    }

    [Theory]
    [InlineData("22%", "22%")]
    [InlineData("10%", "10%")]
    [InlineData("esclusa", "esclusa")]
    [InlineData("RC 10%+22%", "RC 10%+22%")]
    [InlineData("invalid", "22%")]
    public void DefaultVatAcceptsSupportedValues(string configured, string expected) =>
        Assert.Equal(expected, new AppSettingsServiceModel { DefaultVatType = configured }.GetEffectiveDefaultVatType());
}
