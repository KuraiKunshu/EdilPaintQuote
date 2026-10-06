using System.Globalization;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WorkScheduleCostsPathTests
{
    private static readonly Guid InterventionId = Guid.Parse("746e49a6-5f20-45c1-9582-23e8de3dcf3e");
    private static readonly DateTime InterventionDate = new(2026, 10, 6);

    private static StoragePathService Service(string? root = null) => new(new PdfStorageSettingsModel
    {
        RootPath = root ?? Path.Combine(Path.GetTempPath(), "EdilPaintCostsPaths", Guid.NewGuid().ToString("N")),
        CustomerFolderPattern = "{CustomerName}",
        PdfFileNamePattern = "{CustomerName}_{ReferenceName}_{QuoteNumber}.pdf"
    });

    [Fact]
    public void CostsAreStoredDirectlyInConfiguredRootWithoutCustomerOrReferenceFolders()
    {
        string root = Path.Combine(Path.GetTempPath(), "PDF root", Guid.NewGuid().ToString("N"));
        var service = Service(root);

        string path = service.BuildWorkScheduleCostsPdfPath("Q-51", InterventionDate, InterventionId);

        Assert.Equal(Path.Combine(root, "CostiLavori"), service.GetWorkScheduleCostsFolder());
        Assert.Equal(service.GetWorkScheduleCostsFolder(), Path.GetDirectoryName(path));
        Assert.Equal("CostiLavoro_Q-51_20261006_746e49a65f2045c1958223e8de3dcf3e.pdf", Path.GetFileName(path));
        Assert.False(Directory.Exists(root));
        Assert.False(Directory.Exists(service.GetWorkScheduleCostsFolder()));
    }

    [Theory]
    [InlineData("../../outside")]
    [InlineData(@"..\..\outside")]
    [InlineData(@"D:\outside\quote")]
    [InlineData("quote/name:with*invalid?chars<and>|quotes\"")]
    [InlineData(" . ")]
    public void QuoteNumbersCannotChangeTheCostsDestinationFolder(string quoteNumber)
    {
        var service = Service();
        string path = service.BuildWorkScheduleCostsPdfPath(quoteNumber, InterventionDate, InterventionId);

        Assert.Equal(Path.GetFullPath(service.GetWorkScheduleCostsFolder()),
            Path.GetDirectoryName(Path.GetFullPath(path)));
        Assert.Equal(-1, Path.GetFileName(path).IndexOfAny(Path.GetInvalidFileNameChars()));
        Assert.Equal(".pdf", Path.GetExtension(path));
    }

    [Fact]
    public void DifferentVisitsForTheSameQuoteAndDateHaveDifferentFiles()
    {
        var service = Service();
        string first = service.BuildWorkScheduleCostsPdfPath("Q-51", InterventionDate, InterventionId);
        string second = service.BuildWorkScheduleCostsPdfPath("Q-51", InterventionDate, Guid.NewGuid());

        Assert.NotEqual(first, second);
        Assert.Equal(Path.GetDirectoryName(first), Path.GetDirectoryName(second));
    }

    [Fact]
    public void TheSameVisitHasAStablePathRegardlessOfTimeOfDay()
    {
        var service = Service();
        Assert.Equal(service.BuildWorkScheduleCostsPdfPath("Q-51", InterventionDate, InterventionId),
            service.BuildWorkScheduleCostsPdfPath("Q-51", InterventionDate.AddHours(16), InterventionId));
    }

    [Fact]
    public void FileDateUsesTheGregorianCalendarRegardlessOfCurrentCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            Assert.Contains("_20261006_", Service().BuildWorkScheduleCostsPdfPath("Q-51", InterventionDate, InterventionId));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankQuoteNumberIsRejected(string? quoteNumber)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            Service().BuildWorkScheduleCostsPdfPath(quoteNumber!, InterventionDate, InterventionId));
        Assert.Equal("quoteNumber", exception.ParamName);
    }

    [Fact]
    public void MissingDateIsRejected()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            Service().BuildWorkScheduleCostsPdfPath("Q-51", default, InterventionId));
        Assert.Equal("interventionDate", exception.ParamName);
    }

    [Fact]
    public void MissingInterventionIdIsRejected()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            Service().BuildWorkScheduleCostsPdfPath("Q-51", InterventionDate, Guid.Empty));
        Assert.Equal("interventionId", exception.ParamName);
    }

    [Fact]
    public void MissingPdfRootIsRejected()
    {
        var service = new StoragePathService(new PdfStorageSettingsModel());
        Assert.Throws<InvalidOperationException>(() => service.GetWorkScheduleCostsFolder());
        Assert.Throws<InvalidOperationException>(() =>
            service.BuildWorkScheduleCostsPdfPath("Q-51", InterventionDate, InterventionId));
    }
}
