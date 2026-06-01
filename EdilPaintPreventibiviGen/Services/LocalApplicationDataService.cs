using System.Diagnostics;
using System.IO;

namespace EdilPaintPreventibiviGen.Services;

public static class LocalApplicationDataService
{
    private static readonly string[] LegacyDataFiles =
    [
        "azienda.json",
        "clienti.json",
        "config_fatture.json",
        "dati_lavori.json",
        "history.json",
        "materiali_personali.json"
    ];

    public static string EnsureDataDirectory(string legacyAssetsPath)
    {
        string dataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EdilPaintPreventivi",
            "Data");

        Directory.CreateDirectory(dataPath);

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
