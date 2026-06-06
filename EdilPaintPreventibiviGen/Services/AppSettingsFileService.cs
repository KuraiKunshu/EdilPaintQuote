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

    private static void WriteDefaultSettings(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string pdfRootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "EdilPaintPreventivi",
            "Preventivi");

        var settings = new
        {
            ConnectionStrings = new
            {
                DefaultConnection = new
                {
                    ConnectionString = string.Empty,
                    Server = string.Empty,
                    Database = string.Empty,
                    Username = string.Empty,
                    Password = string.Empty
                }
            },
            App = new
            {
                FirstStartup = false,
                GeneratePDF = true,
                RestoreMissingPdfsOnStartup = false,
                IsSilentStartup = false,
                UseVeluxLogin = false,
                NumberOfQuote = 200,
                TempPath = string.Empty,
                DeviceName = Environment.MachineName
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
                NotesTitle = "NOTE E TERMINI DI PAGAMENTO",
                FooterText = string.Empty,
                SignatureText = "Firma per accettazione",
                ShowTemplateName = false
            }
        };

        File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}
