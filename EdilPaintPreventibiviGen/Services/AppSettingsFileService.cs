using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace EdilPaintPreventibiviGen.Services;

public static class AppSettingsFileService
{
    private const string FileName = "appsettings.json";
    private static string? _settingsPath;

    public static string EnsureExists()
    {
        if (_settingsPath != null)
            return _settingsPath;

        if (CompanyInstallationService.IsGenericInstallation)
        {
            string profilePath = Path.Combine(CompanyInstallationService.RootDirectory, FileName);
            if (!File.Exists(profilePath)) WriteDefaultSettings(profilePath, generic: true);
            return _settingsPath = profilePath;
        }

        string applicationPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
        if (File.Exists(applicationPath))
            return _settingsPath = applicationPath;

        string localPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EdilPaintPreventivi",
            FileName);

        if (File.Exists(localPath))
            return _settingsPath = localPath;

        try
        {
            WriteDefaultSettings(applicationPath);
            return _settingsPath = applicationPath;
        }
        catch (UnauthorizedAccessException)
        {
            WriteDefaultSettings(localPath);
            return _settingsPath = localPath;
        }
        catch (IOException)
        {
            WriteDefaultSettings(localPath);
            return _settingsPath = localPath;
        }
    }

    public static IConfigurationRoot BuildConfiguration()
    {
        string path = EnsureExists();
        return new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(path)!)
            .AddJsonFile(Path.GetFileName(path), optional: false, reloadOnChange: false)
            .Build();
    }

    internal static void WriteDefaultSettings(string path, bool generic = false)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string pdfRootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "EdilPaintPreventivi",
            "Preventivi");
        if (generic)
            pdfRootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PreventiviAzienda", CompanyInstallationService.ProfileId ?? "NuovaAzienda");

        var settings = new
        {
            Business = new BusinessSettingsModel
            {
                EnableWindowAutomations = !generic,
                EnableInstallationCertificate = !generic,
                UseLegacyStamp = !generic
            },
            Employees = Array.Empty<EdilPaintPreventibiviGen.Models.EmployeeSettingsModel>(),
            Database = new
            {
                Provider = DatabaseSettingsModel.SqlServerProvider,
                Server = string.Empty,
                Port = (int?)null,
                DatabaseName = string.Empty,
                Username = string.Empty,
                Password = string.Empty
            },
            App = new
            {
                FirstStartup = false,
                GeneratePDF = true,
                RestoreMissingPdfsOnStartup = false,
                DatabaseCostSavingMode = true,
                IsSilentStartup = false,
                UseVeluxLogin = false,
                ImportLegacyData = !generic,
                DefaultVatType = generic ? "22%" : "RC 10%+22%",
                NumberOfQuote = 200,
                TempPath = string.Empty,
                DeviceName = Environment.MachineName
            },
            RealProfit = new
            {
                Workers = RealProfitSettingsModel.DefaultWorkers,
                Days = RealProfitSettingsModel.DefaultDays,
                HoursPerDay = RealProfitSettingsModel.DefaultHoursPerDay,
                HourlyCost = RealProfitSettingsModel.DefaultHourlyCost,
                ProfitReductionPercentage = RealProfitSettingsModel.DefaultProfitReductionPercentage,
                WindowProductPrefixes = RealProfitSettingsModel.CreateDefaultWindowProductPrefixes(),
                WindowMaterialCatalogIdentity = string.Empty,
                WindowMaterialRulesSchemaVersion = RealProfitSettingsModel.CurrentWindowMaterialRulesSchemaVersion,
                WindowMaterialRules = generic ? [] : RealProfitSettingsModel.CreateDefaultWindowMaterialRules(),
                InternalFinishLaborKeyword = RealProfitSettingsModel.DefaultInternalFinishLaborKeyword,
                InternalFinishMaterialName = RealProfitSettingsModel.DefaultInternalFinishMaterialName
            },
            PdfStorage = new
            {
                RootPath = pdfRootPath,
                HistorySubFolder = "Storico",
                CustomerFolderPattern = "{CustomerName}",
                PdfFileNamePattern = "{CustomerName}_Preventivo_{QuoteNumber}_{Date}.pdf"
            },
            PdfTemplate = new
            {
                ActiveTemplate = "Standard",
                NotesTitle = PdfTemplateSettingsModel.DefaultNotesTitle,
                FooterText = string.Empty,
                SignatureText = "Firma per accettazione",
                ShowTemplateName = false
            },
            Mail = new
            {
                Enabled = false,
                SmtpServer = generic ? string.Empty : "smtp.libero.it",
                Port = 465,
                UseSsl = true,
                Username = string.Empty,
                Password = string.Empty,
                SenderEmail = string.Empty,
                SenderName = generic ? "Preventivi" : "EdilPaint",
                DefaultSubject = "Preventivo {QuoteNumber}",
                DefaultBody = "Buongiorno,\n\nin allegato inviamo il preventivo n. {QuoteNumber}.\n\nCordiali saluti",
                SupplierOrderSubjectTemplate = MailSettingsModel.DefaultSupplierOrderSubjectTemplate,
                SupplierOrderBodyTemplate = MailSettingsModel.DefaultSupplierOrderBodyTemplate
            }
        };

        File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}
