using System.Collections.Concurrent;
using System.Reflection;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class QuotePdfDocumentServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EdilPaintQuotePdfTests", Guid.NewGuid().ToString("N"));
    private readonly IDataService _data;
    private readonly DataProxy _fake;
    private readonly AppSettingsServiceModel _settings;
    private readonly PdfTemplateSettingsModel _template = new();
    private string ExpectedPath => Path.Combine(_root, "customer", "quote.pdf");
    private string TemporaryRoot => Path.Combine(_root, "temp");

    public QuotePdfDocumentServiceTests()
    {
        Directory.CreateDirectory(_root);
        _data = DispatchProxy.Create<IDataService, DataProxy>();
        _fake = (DataProxy)_data;
        _settings = new() { GeneratePDF = true, TempPath = TemporaryRoot };
    }

    private QuotePdfDocumentService Service(
        Action<PdfGenerationContext, Company, string>? writer = null,
        Func<IReadOnlyList<Customer>>? customers = null,
        Func<string>? selectedLogo = null) => new(_data, _ => ExpectedPath, _settings, _template,
            selectedLogo, customers, writer ?? ((_, _, path) => File.WriteAllText(path, "fresh-pdf")));

    [Fact]
    public async Task LoadsFullQuoteWithAttachmentsAndCustomersWithoutSavingOrAllocatingNumbers()
    {
        PdfGenerationContext? captured = null;
        _fake.Customers = [new() { BusinessName = "Customer", Address = "Customer address" }];
        string path = await Service((context, _, output) =>
        {
            captured = context;
            File.WriteAllText(output, "fresh-pdf");
        }).GetPdfPathAsync(" Q-51 ");

        Assert.Equal(ExpectedPath, path);
        Assert.Equal("fresh-pdf", File.ReadAllText(path));
        Assert.Equal("Q-51", _fake.RequestedQuote);
        Assert.True(_fake.IncludeAttachments);
        Assert.Equal("Customer address", Assert.Single(captured!.AllCustomers).Address);
        Assert.Equal(new[] { nameof(IDataService.GetQuoteByNumberAsync), nameof(IDataService.GetCompanyAsync),
            nameof(IDataService.GetCustomersAsync) }, _fake.Calls.ToArray());
        Assert.Empty(Directory.GetFiles(TemporaryRoot, "*.pdf"));
    }

    [Fact]
    public void ContextPreservesQuoteTaxTotalsSiteBillingAttachmentsAndTemplateInAnIndependentSnapshot()
    {
        var entry = new QuoteHistoryEntry
        {
            QuoteNumber = "Q-51", Date = new(2026, 10, 6), CustomerName = "Customer", ReferenceName = "Reference",
            SiteName = "Site address", BillingCustomerName = "Billing", PaymentTerms = "30 days",
            CustomerNotes = "Client notes", IvaType = "RC 10%+22%", Imponibile = 1250, Total = 1525,
            MaterialDiscount = 5, LaborDiscount = 10,
            Materials = [new() { Name = "Material", Description = "Description", Quantity = 10000.125m,
                UnitOfMeasure = "m²", UnitPrice = 8.5, Discount = 2, IsSignificant = true,
                IsCompanyMaterial = true, ExcludeFromWorkSheet = true, SortOrder = 4, PersistentId = 17 }],
            Labors = [new() { Name = "Work", Quantity = 2.5m, UnitOfMeasure = "ore", UnitPrice = 40 }],
            Attachments = [new() { FileName = "image.png", ContentType = "image/png", Content = [1, 2, 3],
                ImportedAt = new(2026, 10, 5) }, new() { FileName = "empty.pdf", Content = [] }]
        };
        var template = new PdfTemplateSettingsModel
        {
            ActiveTemplate = "Impresa", NotesTitle = "Additional notes", FooterText = "Footer",
            SignatureText = "Signature", ShowTemplateName = true
        };
        var customers = new List<Customer> { new() { BusinessName = "Customer", Address = "Address",
            Email = "customer@example.test", Phone = "123" }, new() { BusinessName = "Reference" },
            new() { BusinessName = "Billing" } };
        var context = QuotePdfDocumentService.CreateContext(entry, new(), customers, "chosen-logo.png", template);

        Assert.Equal(entry.QuoteNumber, context.QuoteNumber);
        Assert.Equal(entry.Date, context.Date);
        Assert.Equal(entry.PaymentTerms, context.PaymentTerms);
        Assert.Equal(entry.CustomerNotes, context.CustomerNotes);
        Assert.Equal(entry.IvaType, context.IvaType);
        Assert.Equal(entry.CustomerName, context.CustomerName);
        Assert.Equal(entry.ReferenceName, context.ReferenceName);
        Assert.Equal(entry.SiteName, context.SiteName);
        Assert.Equal(entry.BillingCustomerName, context.BillingCustomerName);
        Assert.Equal(entry.Imponibile, context.Imponibile);
        Assert.Equal(entry.Total, context.Total);
        Assert.Equal(entry.MaterialDiscount, context.MaterialDiscount);
        Assert.Equal(entry.LaborDiscount, context.LaborDiscount);
        Assert.Equal("chosen-logo.png", context.SelectedLogo);
        Assert.Equal(template.ActiveTemplate, context.PdfTemplateName);
        Assert.Equal(template.NotesTitle, context.PdfNotesTitle);
        Assert.Equal(template.FooterText, context.PdfFooterText);
        Assert.Equal(template.SignatureText, context.PdfSignatureText);
        Assert.True(context.PdfShowTemplateName);
        Assert.Equal(3, context.AllCustomers.Count);
        Assert.Equal(10000.125m, context.Materials.Single().Quantity);
        Assert.Equal("m²", context.Materials.Single().UnitOfMeasure);
        Assert.True(context.Materials.Single().IsSignificant);
        Assert.True(context.Materials.Single().IsCompanyMaterial);
        Assert.True(context.Materials.Single().ExcludeFromWorkSheet);
        Assert.Equal(4, context.Materials.Single().SortOrder);
        Assert.Equal(17, context.Materials.Single().PersistentId);
        Assert.Equal(2.5m, context.Labors.Single().Quantity);
        var attachment = Assert.Single(context.Attachments);
        Assert.Equal("image.png", attachment.FileName);
        Assert.Equal("image/png", attachment.ContentType);
        Assert.Equal(new DateTime(2026, 10, 5), attachment.ImportedAt);
        Assert.Equal(new byte[] { 1, 2, 3 }, attachment.Content);

        entry.Materials[0].Quantity = 1;
        entry.Attachments[0].Content[0] = 9;
        customers[0].Address = "Changed";
        Assert.Equal(10000.125m, context.Materials.Single().Quantity);
        Assert.Equal(1, attachment.Content[0]);
        Assert.Equal("Address", context.AllCustomers[0].Address);
    }

    [Theory]
    [InlineData(1, "second.png")]
    [InlineData(-1, "first.png")]
    [InlineData(5, "first.png")]
    public void CompanyLogoFallbackMatchesHistory(int index, string expected) =>
        Assert.Equal(expected, QuotePdfDocumentService.CreateContext(new(),
            new() { Logo = ["first.png", "second.png"], Logo_index = index }, [], "", new()).SelectedLogo);

    [Fact]
    public async Task SuppliedCustomerSnapshotAndLogoAreUsedWithoutAnotherCustomerRead()
    {
        PdfGenerationContext? captured = null;
        await Service((context, _, output) => { captured = context; File.WriteAllText(output, "fresh"); },
            () => [new() { BusinessName = "UI customer" }], () => "ui-logo.png").GetPdfPathAsync("Q-51");
        Assert.Equal("UI customer", Assert.Single(captured!.AllCustomers).BusinessName);
        Assert.Equal("ui-logo.png", captured.SelectedLogo);
        Assert.DoesNotContain(nameof(IDataService.GetCustomersAsync), _fake.Calls);
    }

    [Fact]
    public async Task FailedCopyReturnsFreshTemporaryPdfEvenWhenDestinationAlreadyExists()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ExpectedPath)!);
        File.WriteAllText(ExpectedPath, "old-pdf");
        using var locked = new FileStream(ExpectedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        string path = await Service().GetPdfPathAsync("Q-51");
        Assert.NotEqual(ExpectedPath, path);
        Assert.Equal(TemporaryRoot, Path.GetDirectoryName(path));
        Assert.Equal("fresh-pdf", File.ReadAllText(path));
        Assert.Equal("old-pdf", File.ReadAllText(ExpectedPath));
    }

    [Fact]
    public async Task ConcurrentRequestsUseDifferentTemporaryFiles()
    {
        Directory.CreateDirectory(ExpectedPath); // Unavailable official destination.
        using var barrier = new Barrier(2);
        var paths = new ConcurrentBag<string>();
        var service = Service((_, _, output) =>
        {
            paths.Add(output);
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(15)));
            File.WriteAllText(output, output);
        });
        var results = await Task.WhenAll(service.GetPdfPathAsync("Q-51"), service.GetPdfPathAsync("Q-51"));
        Assert.Equal(2, paths.Distinct().Count());
        Assert.Equal(2, results.Distinct().Count());
        Assert.All(results, path =>
        {
            Assert.Equal(TemporaryRoot, Path.GetDirectoryName(path));
            Assert.Equal(path, File.ReadAllText(path));
        });
    }

    [Fact]
    public async Task DisabledGenerationNeverOpensAnExistingOldPdf()
    {
        _settings.GeneratePDF = false;
        Directory.CreateDirectory(Path.GetDirectoryName(ExpectedPath)!);
        File.WriteAllText(ExpectedPath, "old-pdf");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service().GetPdfPathAsync("Q-51"));
        Assert.Contains("disabilitata", error.Message);
        Assert.Equal("old-pdf", File.ReadAllText(ExpectedPath));
        Assert.DoesNotContain(nameof(IDataService.GetCompanyAsync), _fake.Calls);
    }

    [Fact]
    public async Task CancellationBeforeReadingDoesNotLoadOrWriteAnything()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().GetPdfPathAsync("Q-51", cts.Token));
        Assert.Empty(_fake.Calls);
        Assert.False(Directory.Exists(TemporaryRoot));
    }

    [Fact]
    public async Task CancellationAfterCompanyReadDoesNotGenerate()
    {
        using var cts = new CancellationTokenSource();
        _fake.AfterCompanyRead = cts.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().GetPdfPathAsync("Q-51", cts.Token));
        Assert.DoesNotContain(nameof(IDataService.GetCustomersAsync), _fake.Calls);
        Assert.False(Directory.Exists(TemporaryRoot));
    }

    [Fact]
    public async Task CancellationDuringGenerationDoesNotReplaceOldDestinationAndCleansTemporaryFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ExpectedPath)!);
        File.WriteAllText(ExpectedPath, "old-pdf");
        using var cts = new CancellationTokenSource();
        var service = Service((_, _, output) => { File.WriteAllText(output, "fresh-pdf"); cts.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetPdfPathAsync("Q-51", cts.Token));
        Assert.Equal("old-pdf", File.ReadAllText(ExpectedPath));
        Assert.Empty(Directory.GetFiles(TemporaryRoot, "*.pdf"));
    }

    [Fact]
    public async Task FailedGenerationCannotFallBackToOldDestinationAndCleansPartialFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ExpectedPath)!);
        File.WriteAllText(ExpectedPath, "old-pdf");
        var service = Service((_, _, output) => { File.WriteAllText(output, "partial"); throw new InvalidOperationException("failed"); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetPdfPathAsync("Q-51"));
        Assert.Equal("old-pdf", File.ReadAllText(ExpectedPath));
        Assert.Empty(Directory.GetFiles(TemporaryRoot, "*.pdf"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrEmptyGeneratedFileIsRejected(bool emptyFile)
    {
        var service = Service((_, _, output) => { if (emptyFile) File.WriteAllBytes(output, []); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetPdfPathAsync("Q-51"));
        Assert.False(File.Exists(ExpectedPath));
        Assert.Empty(Directory.GetFiles(TemporaryRoot, "*.pdf"));
    }

    [Fact]
    public async Task MissingQuoteIsReportedWithoutGeneratingAnything()
    {
        _fake.Entry = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().GetPdfPathAsync("missing"));
        Assert.Equal(new[] { nameof(IDataService.GetQuoteByNumberAsync) }, _fake.Calls.ToArray());
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    public class DataProxy : DispatchProxy
    {
        public ConcurrentQueue<string> Calls { get; } = new();
        public QuoteHistoryEntry? Entry { get; set; } = new() { QuoteNumber = "Q-51", CustomerName = "Customer", Date = new(2026, 10, 6) };
        public List<Customer> Customers { get; set; } = [];
        public string? RequestedQuote { get; private set; }
        public bool IncludeAttachments { get; private set; }
        public Action? AfterCompanyRead { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            string name = targetMethod!.Name;
            Calls.Enqueue(name);
            switch (name)
            {
                case nameof(IDataService.GetQuoteByNumberAsync):
                    RequestedQuote = (string)args![0]!;
                    IncludeAttachments = (bool)args[2]!;
                    return Task.FromResult(Entry);
                case nameof(IDataService.GetCompanyAsync):
                    AfterCompanyRead?.Invoke();
                    return Task.FromResult<Company?>(new());
                case nameof(IDataService.GetCustomersAsync):
                    return Task.FromResult(Customers);
                default:
                    throw new InvalidOperationException("Unexpected data operation: " + name);
            }
        }
    }
}
