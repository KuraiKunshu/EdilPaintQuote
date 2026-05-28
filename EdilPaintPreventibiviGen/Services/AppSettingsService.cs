using Microsoft.Extensions.Configuration;
using System.IO;

namespace EdilPaintPreventibiviGen.Services;

public sealed class AppSettingsService
{
	public AppSettingsServiceModel App { get; }
	public PdfStorageSettingsModel PdfStorage { get; }

	public AppSettingsService(IConfiguration configuration)
	{
		App = configuration.GetSection("App").Get<AppSettingsServiceModel>() ?? new AppSettingsServiceModel();
		PdfStorage = configuration.GetSection("PdfStorage").Get<PdfStorageSettingsModel>() ?? new PdfStorageSettingsModel();
	}
}

public sealed class AppSettingsServiceModel
{
	public bool FirstStartup { get; set; } = true;
	public bool GeneratePDF { get; set; } = true;
	public bool IsSilentStartup { get; set; } = true;
	public int NumberOfQuote { get; set; } = 100;
	public string TempPath { get; set; } = string.Empty;

	/// <summary>
	/// Restituisce il percorso temp effettivo: quello configurato se valido, altrimenti %TEMP%\EdilPaintPreventivi.
	/// La cartella viene creata automaticamente se non esiste.
	/// </summary>
	public string GetEffectiveTempPath()
	{
		if (!string.IsNullOrWhiteSpace(TempPath))
		{
			try
			{
				Directory.CreateDirectory(TempPath);
				string testFile = Path.Combine(TempPath, ".writetest");
				File.WriteAllText(testFile, "test");
				File.Delete(testFile);
				return TempPath;
			}
			catch
			{
				// Usa il fallback locale se il percorso configurato non e' scrivibile.
			}
		}

		string fallback = Path.Combine(Path.GetTempPath(), "EdilPaintPreventivi");
		Directory.CreateDirectory(fallback);
		return fallback;
	}
}

public sealed class PdfStorageSettingsModel
{
	public string RootPath { get; set; } = string.Empty;
	public string? HistorySubFolder { get; set; }
	public string? CustomerFolderPattern { get; set; }
	public string? PdfFileNamePattern { get; set; }
}
