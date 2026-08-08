using Microsoft.Extensions.Configuration;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace EdilPaintPreventibiviGen.Services;

public sealed class AppSettingsService
{
	public AppSettingsServiceModel App { get; }
	public RealProfitSettingsModel RealProfit { get; }
	public PdfStorageSettingsModel PdfStorage { get; }
	public PdfTemplateSettingsModel PdfTemplate { get; }
	public DatabaseSettingsModel Database { get; }
	public MailSettingsModel Mail { get; }
	public string SettingsPath { get; }

	public AppSettingsService(IConfiguration configuration)
	{
		SettingsPath = AppSettingsFileService.EnsureExists();
		App = configuration.GetSection("App").Get<AppSettingsServiceModel>() ?? new AppSettingsServiceModel();
		RealProfit = configuration.GetSection("RealProfit").Get<RealProfitSettingsModel>() ?? new RealProfitSettingsModel();
		RealProfit.Normalize();
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
		RealProfit.Normalize();
		root.Remove("ConnectionStrings");
		root["Database"] = new JsonObject
		{
			["Provider"] = Database.Provider,
			["Server"] = SecretProtectionService.Protect(Database.Server),
			["Port"] = Database.Port,
			["DatabaseName"] = SecretProtectionService.Protect(Database.Database),
			["Username"] = SecretProtectionService.Protect(Database.Username),
			["Password"] = SecretProtectionService.Protect(Database.Password)
		};
		root["App"] = JsonSerializer.SerializeToNode(App, jsonOptions);
		root["RealProfit"] = JsonSerializer.SerializeToNode(RealProfit, jsonOptions);
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
			["DefaultBody"] = Mail.DefaultBody,
			["SupplierOrderSubjectTemplate"] = Mail.SupplierOrderSubjectTemplate,
			["SupplierOrderBodyTemplate"] = Mail.SupplierOrderBodyTemplate
		};

		string temporaryPath = SettingsPath + ".tmp";
		File.WriteAllText(temporaryPath, root.ToJsonString(jsonOptions));
		File.Move(temporaryPath, SettingsPath, overwrite: true);
	}

	private static DatabaseSettingsModel LoadDatabaseSettings(IConfiguration configuration)
	{
		var section = configuration.GetSection("Database");
		if (!section.GetChildren().Any())
			section = configuration.GetSection("ConnectionStrings:DefaultConnection");

		if (section.GetChildren().Any())
		{
			try
			{
				string provider = DatabaseSettingsModel.NormalizeProvider(section["Provider"]);
				string server = SecretProtectionService.Unprotect(section["Server"] ?? string.Empty);
				int? port = TryReadPort(section["Port"]);
				string database = SecretProtectionService.Unprotect(
					section["DatabaseName"] ??
					section["Database"] ??
					string.Empty);
				string username = SecretProtectionService.Unprotect(section["Username"] ?? string.Empty);
				string password = SecretProtectionService.Unprotect(section["Password"] ?? string.Empty);
				string sectionLegacyConnectionString = SecretProtectionService.Unprotect(section["ConnectionString"] ?? string.Empty);

				if (string.IsNullOrWhiteSpace(server) &&
					string.IsNullOrWhiteSpace(database) &&
					string.IsNullOrWhiteSpace(username) &&
					string.IsNullOrWhiteSpace(password) &&
					string.IsNullOrWhiteSpace(sectionLegacyConnectionString))
				{
					return new DatabaseSettingsModel { Provider = provider, Port = port };
				}

				if (!string.IsNullOrWhiteSpace(sectionLegacyConnectionString))
					ReadLegacyConnectionString(provider, sectionLegacyConnectionString, ref server, ref port, ref database, ref username, ref password);

				return new DatabaseSettingsModel
				{
					Provider = provider,
					Server = server,
					Port = port,
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
			string connectionString = SecretProtectionService.Unprotect(legacyConnectionString);
			string provider = LooksLikePostgreSqlConnectionString(connectionString)
				? DatabaseSettingsModel.PostgreSqlProvider
				: DatabaseSettingsModel.SqlServerProvider;
			string server = string.Empty;
			int? port = null;
			string database = string.Empty;
			string username = string.Empty;
			string password = string.Empty;
			ReadLegacyConnectionString(provider, connectionString, ref server, ref port, ref database, ref username, ref password);

			return new DatabaseSettingsModel
			{
				Provider = provider,
				Server = server,
				Port = port,
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

	private static int? TryReadPort(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return null;

		return int.TryParse(value, out int port) && port > 0 && port <= 65535
			? port
			: null;
	}

	private static void ReadLegacyConnectionString(
		string provider,
		string connectionString,
		ref string server,
		ref int? port,
		ref string database,
		ref string username,
		ref string password)
	{
		if (DatabaseSettingsModel.IsPostgreSqlProvider(provider) || LooksLikePostgreSqlConnectionString(connectionString))
		{
			var builder = new NpgsqlConnectionStringBuilder(connectionString);
			if (string.IsNullOrWhiteSpace(server))
				server = builder.Host ?? string.Empty;
			if (port is null && builder.Port > 0)
				port = builder.Port;
			if (string.IsNullOrWhiteSpace(database))
				database = builder.Database ?? string.Empty;
			if (string.IsNullOrWhiteSpace(username))
				username = builder.Username ?? string.Empty;
			if (string.IsNullOrWhiteSpace(password))
				password = builder.Password ?? string.Empty;
			return;
		}

		var sqlBuilder = new SqlConnectionStringBuilder(connectionString);
		if (string.IsNullOrWhiteSpace(server))
		{
			(server, port) = SplitSqlServerAndPort(sqlBuilder.DataSource);
		}

		if (string.IsNullOrWhiteSpace(database))
			database = sqlBuilder.InitialCatalog ?? string.Empty;
		if (string.IsNullOrWhiteSpace(username))
			username = sqlBuilder.UserID ?? string.Empty;
		if (string.IsNullOrWhiteSpace(password))
			password = sqlBuilder.Password ?? string.Empty;
	}

	private static bool LooksLikePostgreSqlConnectionString(string connectionString)
		=> connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
		   connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
		   connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase);

	private static (string Server, int? Port) SplitSqlServerAndPort(string dataSource)
	{
		if (string.IsNullOrWhiteSpace(dataSource))
			return (string.Empty, null);

		string value = dataSource.Trim();
		int commaIndex = value.LastIndexOf(',');
		if (commaIndex <= 0 || commaIndex == value.Length - 1)
			return (value, null);

		string possiblePort = value[(commaIndex + 1)..];
		return int.TryParse(possiblePort, out int port) && port > 0 && port <= 65535
			? (value[..commaIndex], port)
			: (value, null);
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
	public double MainWindowScale { get; set; } = 1.0;
	public string TempPath { get; set; } = string.Empty;
	public string DeviceName { get; set; } = string.Empty;

	public double GetEffectiveMainWindowScale()
	{
		if (double.IsNaN(MainWindowScale) || double.IsInfinity(MainWindowScale))
			return 1.0;

		return Math.Clamp(MainWindowScale, 0.6, 1.3);
	}

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

public sealed class RealProfitSettingsModel
{
	public const int CurrentWindowMaterialRulesSchemaVersion = 2;
	public const int DefaultWorkers = 2;
	public const double DefaultDays = 1d;
	public const double DefaultHoursPerDay = 10d;
	public const double DefaultHourlyCost = 40d;
	public const double DefaultProfitReductionPercentage = 0d;
	public const string DefaultInternalFinishLaborKeyword = "Finitura interna";
	public const string DefaultInternalFinishMaterialName = "Perline";

	public int Workers { get; set; } = DefaultWorkers;
	public double Days { get; set; } = DefaultDays;
	public double HoursPerDay { get; set; } = DefaultHoursPerDay;
	public double HourlyCost { get; set; } = DefaultHourlyCost;
	public double ProfitReductionPercentage { get; set; } = DefaultProfitReductionPercentage;
	public List<string> WindowProductPrefixes { get; set; } = [];
	public string WindowMaterialCatalogIdentity { get; set; } = string.Empty;
	public int WindowMaterialRulesSchemaVersion { get; set; }
	public List<WindowMaterialRuleSettingsModel> WindowMaterialRules { get; set; } = null!;

	// Campi legacy mantenuti per leggere le configurazioni create dalle versioni precedenti.
	public string InternalFinishLaborKeyword { get; set; } = DefaultInternalFinishLaborKeyword;
	public string InternalFinishMaterialName { get; set; } = DefaultInternalFinishMaterialName;

	public static List<string> CreateDefaultWindowProductPrefixes() =>
		["GGL", "GGU", "GPL", "GPU", "Q4", "R8"];

	public static List<WindowMaterialRuleSettingsModel> CreateDefaultWindowMaterialRules() =>
	[
		new WindowMaterialRuleSettingsModel
		{
			Enabled = true,
			IsWindowAutomation = true,
			LaborName = DefaultInternalFinishLaborKeyword,
			MaterialName = DefaultInternalFinishMaterialName,
			CalculationMode = WindowMaterialRuleSettingsModel.PerimeterCalculationMode,
			QuantityParameter = 1m
		}
	];

	public void Normalize()
	{
		if (Workers < 1)
			Workers = DefaultWorkers;
		if (!double.IsFinite(Days) || Days <= 0)
			Days = DefaultDays;
		if (!double.IsFinite(HoursPerDay) || HoursPerDay <= 0)
			HoursPerDay = DefaultHoursPerDay;
		if (!double.IsFinite(HourlyCost) || HourlyCost < 0)
			HourlyCost = DefaultHourlyCost;
		if (!double.IsFinite(ProfitReductionPercentage))
			ProfitReductionPercentage = DefaultProfitReductionPercentage;
		ProfitReductionPercentage = Math.Clamp(ProfitReductionPercentage, 0, 100);

		WindowProductPrefixes ??= [];
		if (WindowProductPrefixes.Count == 0)
			WindowProductPrefixes = CreateDefaultWindowProductPrefixes();
		WindowProductPrefixes = WindowProductPrefixes
			.Select(prefix => prefix?.Trim().ToUpperInvariant() ?? string.Empty)
			.Where(prefix => prefix.Length > 0)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		WindowMaterialCatalogIdentity = WindowMaterialCatalogIdentity?.Trim() ?? string.Empty;

		string legacyLaborName = InternalFinishLaborKeyword?.Trim() ?? string.Empty;
		string legacyMaterialName = string.IsNullOrWhiteSpace(InternalFinishMaterialName)
			? DefaultInternalFinishMaterialName
			: InternalFinishMaterialName.Trim();

		if (WindowMaterialRulesSchemaVersion < CurrentWindowMaterialRulesSchemaVersion)
		{
			// La lista non esisteva nel vecchio schema. Una lista vuota senza versione
			// viene quindi migrata dalla coppia legacy; dal schema v1 in poi, invece,
			// una lista vuota rappresenta la scelta esplicita di non avere regole.
			if (WindowMaterialRules == null || WindowMaterialRules.Count == 0)
			{
				WindowMaterialRules = legacyLaborName.Length == 0
					? []
					:
					[
						new WindowMaterialRuleSettingsModel
						{
							Enabled = true,
							IsWindowAutomation = true,
							LaborName = legacyLaborName,
							MaterialName = legacyMaterialName,
							CalculationMode = WindowMaterialRuleSettingsModel.PerimeterCalculationMode,
							QuantityParameter = 1m
						}
					];
			}

			WindowMaterialRulesSchemaVersion = CurrentWindowMaterialRulesSchemaVersion;
		}
		else
		{
			WindowMaterialRules ??= [];
		}

		foreach (WindowMaterialRuleSettingsModel rule in WindowMaterialRules)
			rule.Normalize();

		WindowMaterialRulesSchemaVersion = Math.Max(
			WindowMaterialRulesSchemaVersion,
			CurrentWindowMaterialRulesSchemaVersion);

		// Mantiene un fallback leggibile anche per una versione precedente dell'app.
		WindowMaterialRuleSettingsModel? firstEnabledRule =
			WindowMaterialRules.FirstOrDefault(rule => rule.Enabled);
		InternalFinishLaborKeyword = firstEnabledRule?.LaborName ?? string.Empty;
		InternalFinishMaterialName = firstEnabledRule?.MaterialName ?? string.Empty;
	}
}

public sealed class WindowMaterialRuleSettingsModel
{
	public const string PerimeterCalculationMode = "Perimeter";
	public const string FixedPerWindowCalculationMode = "FixedPerWindow";

	public bool Enabled { get; set; } = true;
	public bool IsWindowAutomation { get; set; } = true;
	public int? LaborCatalogId { get; set; }
	public string LaborName { get; set; } = string.Empty;
	public int? MaterialCatalogId { get; set; }
	public string MaterialName { get; set; } = string.Empty;
	public string CalculationMode { get; set; } = PerimeterCalculationMode;
	public decimal QuantityParameter { get; set; } = 1m;

	public void Normalize()
	{
		if (LaborCatalogId <= 0)
			LaborCatalogId = null;
		if (MaterialCatalogId <= 0)
			MaterialCatalogId = null;
		LaborName = LaborName?.Trim() ?? string.Empty;
		MaterialName = MaterialName?.Trim() ?? string.Empty;
		CalculationMode = string.Equals(
			CalculationMode?.Trim(),
			FixedPerWindowCalculationMode,
			StringComparison.OrdinalIgnoreCase)
			? FixedPerWindowCalculationMode
			: PerimeterCalculationMode;
		if (!IsWindowAutomation)
			CalculationMode = FixedPerWindowCalculationMode;
		if (QuantityParameter <= 0)
			QuantityParameter = 1m;
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
	public const string DefaultNotesTitle = "NOTE PER IL CLIENTE";
	private const string LegacyCombinedNotesTitle = "NOTE E TERMINI DI PAGAMENTO";

	public static readonly string[] AvailableTemplates =
	[
		"Standard",
		"Compatto",
		"Collaborazione",
		"Cliente privato",
		"Impresa"
	];

	public string ActiveTemplate { get; set; } = "Standard";
	public string NotesTitle { get; set; } = DefaultNotesTitle;
	public string FooterText { get; set; } = string.Empty;
	public string SignatureText { get; set; } = "Firma per accettazione";
	public bool ShowTemplateName { get; set; }

	public void Normalize()
	{
		if (string.IsNullOrWhiteSpace(ActiveTemplate))
			ActiveTemplate = "Standard";
		NotesTitle = NotesTitle?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(NotesTitle) ||
		    string.Equals(NotesTitle, LegacyCombinedNotesTitle, StringComparison.OrdinalIgnoreCase))
		{
			NotesTitle = DefaultNotesTitle;
		}
		if (string.IsNullOrWhiteSpace(SignatureText))
			SignatureText = "Firma per accettazione";
		FooterText ??= string.Empty;
	}
}

public sealed class MailSettingsModel
{
	public const string ProtectionPurpose = "MailSettings.v1";
	public const string DefaultSupplierOrderSubjectTemplate = "Ordine Riferimento {OrderReference}";
	public const string DefaultSupplierOrderBodyTemplate = "{Materials}";

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
	public string SupplierOrderSubjectTemplate { get; set; } = DefaultSupplierOrderSubjectTemplate;
	public string SupplierOrderBodyTemplate { get; set; } = DefaultSupplierOrderBodyTemplate;
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
		SupplierOrderSubjectTemplate = string.IsNullOrWhiteSpace(SupplierOrderSubjectTemplate)
			? DefaultSupplierOrderSubjectTemplate
			: SupplierOrderSubjectTemplate.Trim();
		SupplierOrderBodyTemplate = string.IsNullOrWhiteSpace(SupplierOrderBodyTemplate)
			? DefaultSupplierOrderBodyTemplate
			: SupplierOrderBodyTemplate;
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
	public const string SqlServerProvider = "SqlServer";
	public const string PostgreSqlProvider = "PostgreSql";
	public static readonly string[] AvailableProviders = [SqlServerProvider, PostgreSqlProvider];

	public string Provider { get; set; } = SqlServerProvider;
	public string Server { get; set; } = string.Empty;
	public int? Port { get; set; }
	public string Database { get; set; } = string.Empty;
	public string Username { get; set; } = string.Empty;
	public string Password { get; set; } = string.Empty;
	public bool RequiresCredentialReset { get; set; }

	public bool IsConfigured =>
		!string.IsNullOrWhiteSpace(Server) ||
		!string.IsNullOrWhiteSpace(Database) ||
		!string.IsNullOrWhiteSpace(Username) ||
		!string.IsNullOrWhiteSpace(Password);

	public static string NormalizeProvider(string? provider)
	{
		if (string.Equals(provider, PostgreSqlProvider, StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
		{
			return PostgreSqlProvider;
		}

		return SqlServerProvider;
	}

	public static bool IsPostgreSqlProvider(string? provider)
		=> string.Equals(NormalizeProvider(provider), PostgreSqlProvider, StringComparison.Ordinal);

	public bool UsesPostgreSql => IsPostgreSqlProvider(Provider);

	public string GetCatalogIdentity()
	{
		string normalizedProvider = NormalizeProvider(Provider?.Trim());
		bool usesPostgreSql = IsPostgreSqlProvider(normalizedProvider);
		string provider = normalizedProvider.ToUpperInvariant();
		string server = Server?.Trim().ToUpperInvariant() ?? string.Empty;
		string databaseName = Database?.Trim() ?? string.Empty;
		bool hasConfiguredCoordinates =
			server.Length > 0 || databaseName.Length > 0 || Port is > 0;
		if (!hasConfiguredCoordinates)
			return string.Empty;

		string database = usesPostgreSql
			? databaseName
			: databaseName.ToUpperInvariant();
		string port = Port is > 0
			? Port.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
			: usesPostgreSql
				? "5432"
				: string.Empty;

		string canonicalIdentity = string.Join('\n', provider, server, port, database);
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalIdentity));
		return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
	}

	public string BuildConnectionString()
		=> UsesPostgreSql ? BuildPostgreSqlConnectionString() : BuildSqlServerConnectionString();

	private string BuildSqlServerConnectionString()
	{
		if (!IsConfigured)
			throw new InvalidOperationException("Connessione SQL non configurata. Inserisci server, database, user e password dalla schermata Impostazioni.");

		var builder = new SqlConnectionStringBuilder();

		if (!string.IsNullOrWhiteSpace(Server))
			builder.DataSource = Port is > 0 && !Server.Contains(',', StringComparison.Ordinal)
				? $"{Server},{Port.Value}"
				: Server;
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

	private string BuildPostgreSqlConnectionString()
	{
		if (!IsConfigured)
			throw new InvalidOperationException("Connessione PostgreSQL non configurata. Inserisci host, porta, database, user e password dalla schermata Impostazioni.");

		var builder = new NpgsqlConnectionStringBuilder();

		if (!string.IsNullOrWhiteSpace(Server))
			builder.Host = Server;
		if (Port is > 0)
			builder.Port = Port.Value;
		if (!string.IsNullOrWhiteSpace(Database))
			builder.Database = Database;
		if (!string.IsNullOrWhiteSpace(Username))
			builder.Username = Username;
		if (!string.IsNullOrWhiteSpace(Password))
			builder.Password = Password;

		if (builder.Port <= 0)
			builder.Port = 5432;
		if (builder.Timeout <= 0)
			builder.Timeout = 30;
		if (builder.CommandTimeout <= 0)
			builder.CommandTimeout = 30;
		if (builder.SslMode == SslMode.Prefer)
			builder.SslMode = SslMode.Require;

		return builder.ConnectionString;
	}
}
