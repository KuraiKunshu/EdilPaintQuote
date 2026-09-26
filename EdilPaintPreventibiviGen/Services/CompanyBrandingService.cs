using System.IO;
using System.Windows.Media.Imaging;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public static class CompanyBrandingService
{
    public static string BrandingDirectory => Path.Combine(CompanyInstallationService.RootDirectory, "Data", "Branding");

    public static string ImportImage(string source)
    {
        // Decode before copying: a file extension alone does not guarantee a valid image.
        using (var stream = File.OpenRead(source))
            _ = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        string extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg"))
            throw new InvalidOperationException("Scegli un'immagine PNG o JPG.");
        Directory.CreateDirectory(BrandingDirectory);
        string stem = Path.GetFileNameWithoutExtension(source);
        if (stem.Length > 40) stem = stem[..40];
        string name = stem + "-" + Guid.NewGuid().ToString("N") + extension;
        File.Copy(source, Path.Combine(BrandingDirectory, name));
        return name;
    }

    public static string ResolveLogo(Company company, string? selectedLogo, string assetsPath) =>
        ResolveLogo(company, selectedLogo, BrandingDirectory, assetsPath, CompanyInstallationService.IsGenericInstallation);

    internal static string ResolveLogo(Company company, string? selectedLogo, string brandingPath, string assetsPath, bool generic)
    {
        if (string.IsNullOrWhiteSpace(selectedLogo)) return string.Empty;
        string name = Path.GetFileName(selectedLogo);
        string local = Path.Combine(brandingPath, name);
        if (File.Exists(local)) return local;
        // A clean company package never falls back to another installation's assets.
        if (generic) return string.Empty;
        string? original = company.Logo.FirstOrDefault(p => Path.GetFileName(p).Equals(name, StringComparison.OrdinalIgnoreCase));
        if (original != null && Path.IsPathRooted(original) && File.Exists(original)) return original;
        string bundled = Path.Combine(assetsPath, name);
        return File.Exists(bundled) ? bundled : string.Empty;
    }

    public static string ResolveConfiguredStamp()
    {
        string? name = App.AppSettings?.Business.StampFileName;
        return string.IsNullOrWhiteSpace(name) ? string.Empty : Path.Combine(BrandingDirectory, Path.GetFileName(name));
    }
}
