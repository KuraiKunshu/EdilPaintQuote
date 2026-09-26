using System.Diagnostics;
using System.IO;

namespace EdilPaintPreventibiviGen.Services;

public static class LocalApplicationDataService
{
    public static string GetDataDirectoryPath()
    {
        return Path.Combine(CompanyInstallationService.RootDirectory, "Data");
    }

    private static readonly string[] LegacyDataFiles =
    [
        "azienda.json",
        "clienti.json",
        "config_fatture.json",
        "dati_lavori.json",
        "history.json",
        "materiali_personali.json"
    ];

    public static string EnsureDataDirectory(string legacyAssetsPath, bool importLegacyData = true)
        => InitializeDataDirectory(legacyAssetsPath, GetDataDirectoryPath(),
            importLegacyData && !CompanyInstallationService.IsGenericInstallation);

    internal static string InitializeDataDirectory(string legacyAssetsPath, string dataPath, bool importLegacyData)
    {
        Directory.CreateDirectory(dataPath);

        if (!importLegacyData)
            return dataPath;

        foreach (string fileName in LegacyDataFiles)
        {
            string sourcePath = Path.Combine(legacyAssetsPath, fileName);
            string destinationPath = Path.Combine(dataPath, fileName);

            if (!File.Exists(destinationPath) && File.Exists(sourcePath))
            {
                File.Copy(sourcePath, destinationPath);
                Debug.WriteLine($"[LocalData] Imported legacy seed: {fileName}");
            }
        }

        return dataPath;
    }
}
