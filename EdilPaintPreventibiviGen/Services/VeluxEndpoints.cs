using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

internal static class VeluxEndpoints
{
    public static VeluxSettingsModel GetSettings() => (App.AppSettings?.Velux ?? new()).CreateValidatedCopy();
    public static Uri BaseUri => GetSettings().BaseUri;
    public static Uri LoginUri => GetSettings().LoginUri;
    public static Uri CatalogUri => GetSettings().CatalogUri;

    public static Uri SearchProductsUri(string term) => GetSettings().SearchProductsUri(term);

    public static Uri ProductDetailsUri(string uuid) => GetSettings().ProductDetailsUri(uuid);
}
