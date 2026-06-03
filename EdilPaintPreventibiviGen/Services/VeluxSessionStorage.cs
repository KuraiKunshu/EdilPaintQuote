using System;
using System.IO;

namespace EdilPaintPreventibiviGen.Services;

public static class VeluxSessionStorage
{
    private const string ProtectionPurpose = "VeluxSession.v1";

    public static string GetStoragePath()
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EdilPaintPreventivi");

        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "velux_storage.json");
    }

    public static string? Load()
    {
        string path = GetStoragePath();
        if (!File.Exists(path))
            return null;

        string storedValue = File.ReadAllText(path);
        string json = SecretProtectionService.Unprotect(storedValue, ProtectionPurpose);

        if (!storedValue.StartsWith("dpapi:", StringComparison.Ordinal))
            Save(json);

        return json;
    }

    public static void Save(string json)
    {
        File.WriteAllText(GetStoragePath(), SecretProtectionService.Protect(json, ProtectionPurpose));
    }

    public static void Clear()
    {
        string path = GetStoragePath();
        if (File.Exists(path))
            File.Delete(path);
    }
}
