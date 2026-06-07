using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;

namespace EdilPaintPreventibiviGen.Services;

public sealed class AppSettingsService
{
	public AppSettingsServiceModel App { get; }
	public PdfStorageSettingsModel PdfStorage { get; }
	public PdfTemplateSettingsModel PdfTemplate { get; }
	public DatabaseSettingsModel Database { get; }
	public MailSettingsModel Mail { get; }
	public string SettingsPath { get; }

	public AppSettingsService(IConfiguration configuration)
	{
		SettingsPath = AppSettingsFileService.EnsureExists();
		App = configuration.GetSection("App").Get<AppSettingsServiceModel>() ?? new AppSettingsServiceModel();
		PdfStorage = configuration.GetSection("PdfStorage").Get<PdfStorageSettingsModel>() ?? new PdfStorageSettingsModel();
		PdfTemplate = configuration.GetSection("PdfTemplate").Get<PdfTemplateSettingsModel>() ?? new PdfTemplateSettingsModel();
		PdfTemplate.Normalize();
		Database = LoadDatabaseSettings(configuration);
		Mail = LoadMailSettings(configuration);
	}

	public void Save()
	{
		var root = File.Exists(SettingsPath)
			? JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject ?? new JsonObject()
			: new JsonObject();

		var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
		root["ConnectionStrings"] = new JsonObject
		{
			["DefaultConnection"] = new JsonObject
			{
				["ConnectionString"] = SecretProtectionService.Protect(Database.ConnectionString),
				["Server"] = SecretProtectionService.Protect(Database.Server),
				["Database"] = SecretProtectionService.Protect(Database.Database),
				["Username"] = SecretProtectionService.Protect(Database.Username),
				["Password"] = SecretProtectionService.Protect(Database.Password)
			}
		};
		root["App"] = JsonSerializer.SerializeToNode(App, jsonOptions);
		root["PdfStorage"] = JsonSerializer.SerializeToNode(PdfStorage, jsonOptions);
		root["PdfTemplate"] = JsonSerializer.SerializeToNode(PdfTemplate, jsonOptions);
		root["Mail"] = new JsonObject
		{
			["Enabled"] = Mail.Enabled,
			["SmtpServer"] = Mail.SmtpServer,
			["Port"] = Mail.Port,
			["UseSsl"] = Mail.UseSsl,
			["Username"] = Mail.Username,
			["Password"] = SecretProtectionService.Protect(Mail.Password, MailSettingsModel.ProtectionPurpose),
			["SenderEmail"] = Mail.SenderEmail,
			["SenderName"] = Mail.SenderName,
			["DefaultSubject"] = Mail.DefaultSubject,
			["DefaultBody"] = Mail.DefaultBody
		};

		string temporaryPath = SettingsPath + ".tmp";
		File.WriteAllText(temporaryPath, root.ToJsonString(jsonOptions));
		File.Move(temporaryPath, SettingsPath, overwrite: true);
	}

	private static DatabaseSettingsModel LoadDatabaseSettings(IConfiguration configuration)
	{
		var section = configuration.GetSection("ConnectionStrings:DefaultConnection");
		if (section.GetChildren().Any())
		{
			try
			{
				string server = SecretProtectionService.Unprotect(section["Server"] ?? string.Empty);
				string database = SecretProtectionService.Unprotect(section["Database"] ?? string.Empty);
				string username = SecretProtectionService.Unprotect(section["Username"] ?? string.Empty);
				string password = SecretProtectionService.Unprotect(section["Password"] ?? string.Empty);
				string connectionString = SecretProtectionService.Unprotect(section["ConnectionString"] ?? string.Empty);

				if (string.IsNullOrWhiteSpace(server) &&
					string.IsNullOrWhiteSpace(database) &&
					string.IsNullOrWhiteSpace(username) &&
					string.IsNullOrWhiteSpace(password) &&
					string.IsNullOrWhiteSpace(connectionString))
				{
					return new DatabaseSettingsModel();
				}

				if (string.IsNullOrWhiteSpace(connectionString))
				{
					var legacyBuilder = new SqlConnectionStringBuilder
					{
						DataSource = server,
						InitialCatalog = database,
						UserID = username,
						Password = password
					};

					connectionString = legacyBuilder.ConnectionString;
				}

				return new DatabaseSettingsModel
				{
					ConnectionString = connectionString,
					Server = server,
					Database = database,
					Username = username,
					Password = password
				};
			}
			catch (InvalidOperationException)
			{
				return new DatabaseSettingsModel { RequiresCredentialReset = true };
			}
		}

		string? legacyConnectionString = configuration.GetConnectionString("DefaultConnection");
		if (string.IsNullOrWhiteSpace(legacyConnectionString))
			return new DatabaseSettingsModel();

		try
		{
			return new DatabaseSettingsModel
			{
				ConnectionString = SecretProtectionService.Unprotect(legacyConnectionString)
			};
		}
		catch (InvalidOperationException)
		{
			return new DatabaseSettingsModel { RequiresCredentialReset = true };
		}
	}

	private static MailSettingsModel LoadMailSettings(IConfiguration configuration)
	{
		var section = configuration.GetSection("Mail");
		if (!section.Exists())
			return new MailSettingsModel();

		var settings = section.Get<MailSettingsModel>() ?? new MailSettingsModel();
		try
		{
			settings.Password = SecretProtectionService.Unprotect(
				section["Password"] ?? string.Empty,
				MailSettingsModel.ProtectionPurpose);
		}
		catch (InvalidOperationException)
		{
			settings.Password = string.Empty;
			settings.RequiresCredentialReset = true;
		}

		settings.Normalize();
		return settings;
	}
}

public sealed class AppSettingsServiceModel
{
	public bool FirstStartup { get; set; } = true;
	public bool GeneratePDF { get; set; } = true;
	public bool RestoreMissingPdfsOnStartup { get; set; }
	public bool DatabaseCostSavingMode { get; set; } = true;
	public bool IsSilentStartup { get; set; } = true;
	public bool UseVeluxLogin { get; set; }
	public int NumberOfQuote { get; set; } = 100;
	public string TempPath { get; set; } = string.Empty;
	public string DeviceName { get; set; } = string.Empty;

	public string GetEffectiveDeviceName()
	{
		if (!string.IsNullOrWhiteSpace(DeviceName))
			return DeviceName.Trim();

		try
		{
			return Environment.MachineName;
		}
		catch
		{
			return "PC sconosciuto";
		}
	}

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

public sealed class PdfTemplateSettingsModel
{
	public static readonly string[] AvailableTemplates =
	[
		"Standard",
		"Compatto",
		"Collaborazione",
		"Cliente privato",
		"Impresa"
	];

	public string ActiveTemplate { get; set; } = "Standard";
	public string NotesTitle { get; set; } = "NOTE E TERMINI DI PAGAMENTO";
	public string FooterText { get; set; } = string.Empty;
	public string SignatureText { get; set; } = "Firma per accettazione";
	public bool ShowTemplateName { get; set; }

	public void Normalize()
	{
		if (string.IsNullOrWhiteSpace(ActiveTemplate))
			ActiveTemplate = "Standard";
		if (string.IsNullOrWhiteSpace(NotesTitle))
			NotesTitle = "NOTE E TERMINI DI PAGAMENTO";
		if (string.IsNullOrWhiteSpace(SignatureText))
			SignatureText = "Firma per accettazione";
		FooterText ??= string.Empty;
	}
}

public sealed class MailSettingsModel
{
	public const string ProtectionPurpose = "MailSettings.v1";

	public bool Enabled { get; set; }
	public string SmtpServer { get; set; } = "smtp.libero.it";
	public int Port { get; set; } = 465;
	public bool UseSsl { get; set; } = true;
	public string Username { get; set; } = string.Empty;
	public string Password { get; set; } = string.Empty;
	public string SenderEmail { get; set; } = string.Empty;
	public string SenderName { get; set; } = "EdilPaint";
	public string DefaultSubject { get; set; } = "Preventivo {QuoteNumber}";
	public string DefaultBody { get; set; } = "Buongiorno,\n\nin allegato inviamo il preventivo n. {QuoteNumber}.\n\nCordiali saluti";
	public bool RequiresCredentialReset { get; set; }

	public string EffectiveSenderEmail =>
		string.IsNullOrWhiteSpace(SenderEmail) ? Username.Trim() : SenderEmail.Trim();

	public void Normalize()
	{
		SmtpServer = string.IsNullOrWhiteSpace(SmtpServer) ? "smtp.libero.it" : SmtpServer.Trim();
		Port = Port <= 0 ? 465 : Port;
		Username = Username?.Trim() ?? string.Empty;
		SenderEmail = SenderEmail?.Trim() ?? string.Empty;
		SenderName = string.IsNullOrWhiteSpace(SenderName) ? "EdilPaint" : SenderName.Trim();
		DefaultSubject = string.IsNullOrWhiteSpace(DefaultSubject)
			? "Preventivo {QuoteNumber}"
			: DefaultSubject.Trim();
		DefaultBody = string.IsNullOrWhiteSpace(DefaultBody)
			? "Buongiorno,\n\nin allegato inviamo il preventivo n. {QuoteNumber}.\n\nCordiali saluti"
			: DefaultBody;
	}

	public void ValidateForSend()
	{
		Normalize();

		if (!Enabled)
			throw new InvalidOperationException("Invio email non abilitato nelle impostazioni.");
		if (string.IsNullOrWhiteSpace(SmtpServer))
			throw new InvalidOperationException("Server SMTP non configurato.");
		if (Port <= 0 || Port > 65535)
			throw new InvalidOperationException("Porta SMTP non valida.");
		if (string.IsNullOrWhiteSpace(Username))
			throw new InvalidOperationException("User SMTP non configurato.");
		if (string.IsNullOrWhiteSpace(Password))
			throw new InvalidOperationException("Password SMTP non configurata.");
		if (string.IsNullOrWhiteSpace(EffectiveSenderEmail))
			throw new InvalidOperationException("Email mittente non configurata.");
	}
}

public sealed class DatabaseSettingsModel
{
	public string ConnectionString { get; set; } = string.Empty;
	public string Server { get; set; } = string.Empty;
	public string Database { get; set; } = string.Empty;
	public string Username { get; set; } = string.Empty;
	public string Password { get; set; } = string.Empty;
	public bool RequiresCredentialReset { get; set; }

	public bool IsConfigured =>
		!string.IsNullOrWhiteSpace(ConnectionString) ||
		!string.IsNullOrWhiteSpace(Server) ||
		!string.IsNullOrWhiteSpace(Database) ||
		!string.IsNullOrWhiteSpace(Username) ||
		!string.IsNullOrWhiteSpace(Password);

	public string BuildConnectionString()
	{
		if (!IsConfigured)
			throw new InvalidOperationException("Connection string SQL non configurata. Inseriscila dalla schermata Impostazioni.");

		SqlConnectionStringBuilder builder;
		try
		{
			builder = new SqlConnectionStringBuilder(ConnectionString ?? string.Empty);
		}
		catch (ArgumentException ex)
		{
			throw new InvalidOperationException("La connection string SQL non e' valida.", ex);
		}

		if (!string.IsNullOrWhiteSpace(Server))
			builder.DataSource = Server;
		if (!string.IsNullOrWhiteSpace(Database))
			builder.InitialCatalog = Database;
		if (!string.IsNullOrWhiteSpace(Username))
			builder.UserID = Username;
		if (!string.IsNullOrWhiteSpace(Password))
			builder.Password = Password;

		if (!builder.ShouldSerialize("Persist Security Info"))
			builder.PersistSecurityInfo = false;
		if (!builder.ShouldSerialize("Multiple Active Result Sets"))
			builder.MultipleActiveResultSets = false;
		if (!builder.ShouldSerialize("Encrypt"))
			builder.Encrypt = true;
		if (!builder.ShouldSerialize("Trust Server Certificate"))
			builder.TrustServerCertificate = false;
		if (!builder.ShouldSerialize("Connect Timeout"))
			builder.ConnectTimeout = 30;

		return builder.ConnectionString;
	}
}
