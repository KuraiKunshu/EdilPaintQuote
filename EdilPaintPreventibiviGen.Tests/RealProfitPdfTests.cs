using System.Text;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class RealProfitPdfTests
{
    [Fact]
    public void GenerateRealProfitPdfCreatesAValidPdf()
    {
        string? requestedOutput = Environment.GetEnvironmentVariable("EDILPAINT_REALPROFIT_PDF_OUTPUT");
        bool keepOutput = !string.IsNullOrWhiteSpace(requestedOutput);
        string outputFolder = keepOutput
            ? Path.GetDirectoryName(Path.GetFullPath(requestedOutput!))!
            : Path.Combine(Path.GetTempPath(), $"EdilPaintRealProfitPdf_{Guid.NewGuid():N}");
        string outputPath = keepOutput
            ? Path.GetFullPath(requestedOutput!)
            : Path.Combine(outputFolder, "GuadagnoReale_1678403.pdf");
        Directory.CreateDirectory(outputFolder);

        try
        {
            var input = new RealProfitInput
            {
                QuoteRevenue = 22288.50,
                SupplierDiscount = 0,
                Workers = 4,
                Days = 2,
                HoursPerDay = 10,
                HourlyCost = 40,
                Materials =
                [
                    Material("GGL MK04 2070(78x98)", 7, 474),
                    Material("GGL MK04 207021A(78x98)", 5, 973),
                    Material("EDW MK04 2000S(78x98)", 12, 154),
                    Material("DKL MK04 1025SG", 7, 103),
                    Material("GGL MK08 2070(78x140)", 2, 551),
                    Material("EDW MK08 2000S(78x140)", 2, 172),
                    Material("DKL MK08 1025SG", 2, 119),
                    Material("GGL PK04 2070(94x98)", 1, 541),
                    Material("EDW PK04 2000S(94x98)", 1, 173),
                    Material("DKL PK04 1025S", 1, 118)
                ],
                CompanyMaterials =
                [
                    new CompanyMaterialCost
                    {
                        Name = "Angolari al ML",
                        Quantity = 62,
                        UnitCost = 9.64,
                        Source = "Automatico"
                    },
                    new CompanyMaterialCost
                    {
                        Name = "Nastro RIWEGA Tape al metro lineare",
                        Quantity = 122,
                        UnitCost = 1.60,
                        Source = "Automatico"
                    },
                    new CompanyMaterialCost
                    {
                        Name = "Perlina al metro lineare",
                        Quantity = 62,
                        UnitCost = 2.03,
                        Source = "Automatico"
                    }
                ]
            };
            RealProfitResult result = RealProfitCalculator.Calculate(input);

            new PdfService().GenerateRealProfitPdf(new RealProfitPdfContext
            {
                QuoteNumber = "1678403",
                QuoteDate = new DateTime(2026, 7, 22),
                CustomerName = "EUROCOLOR SRL DI SAIMON",
                GeneratedAt = new DateTime(2026, 8, 8, 10, 30, 0),
                Input = input,
                Result = result
            }, outputPath);

            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 5_000);
            Assert.Equal(4901.76, result.Profit, 2);
            byte[] header = File.ReadAllBytes(outputPath)[..5];
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(header));
        }
        finally
        {
            if (!keepOutput && Directory.Exists(outputFolder))
                Directory.Delete(outputFolder, recursive: true);
        }
    }

    private static ProfitMaterialCost Material(string name, int quantity, double unitPrice) => new()
    {
        Name = name,
        Quantity = quantity,
        CustomerUnitPrice = unitPrice,
        CustomerDiscount = 25
    };
}
