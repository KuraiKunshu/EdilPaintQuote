using System.IO;

namespace EdilPaintPreventibiviGen.Services;

/// <summary>A clean company distribution has an explicit identity; legacy installs retain their paths.</summary>
public static class CompanyInstallationService
{
    public const string MarkerFileName = "company-profile.id";
    public static string? ProfileId { get; } = ReadProfileId(AppContext.BaseDirectory, IsCompanyBuild);
    private static bool IsCompanyBuild => typeof(CompanyInstallationService).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
        .OfType<System.Reflection.AssemblyMetadataAttribute>()
        .Any(a => a.Key == "GenericCompanyPackage" && a.Value == "true");
    public static bool IsGenericInstallation => ProfileId != null;
    public static string RootDirectory => GetRootDirectory(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProfileId);

    internal static string? ReadProfileId(string directory, bool requireMarker = false)
    {
        string path = Path.Combine(directory, MarkerFileName);
        if (!File.Exists(path))
        {
            if (requireMarker) throw new InvalidOperationException("Manca company-profile.id. Ripristina il file dal pacchetto della tua azienda.");
            return null;
        }
        if (!Guid.TryParse(File.ReadAllText(path).Trim(), out var id) || id == Guid.Empty)
            throw new InvalidOperationException("Identificativo dell'installazione aziendale non valido.");
        return id.ToString("N");
    }

    internal static string GetRootDirectory(string localAppData, string? profileId)
        => profileId == null ? Path.Combine(localAppData, "EdilPaintPreventivi")
            : Path.Combine(localAppData, "PreventiviAzienda", Guid.Parse(profileId).ToString("N"));
}
