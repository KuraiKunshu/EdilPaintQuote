using System.Text.Json.Serialization;

namespace EdilPaintPreventibiviGen.Models;

public sealed record VeluxSettingsModel
{
    public const string DefaultBaseUrl = "https://appvelux.devdemo.it/";
    public const string DefaultLoginPath = "/cas/login";
    public const string DefaultCatalogPath = "/preventivi/it/backoffice/";
    public const string DefaultSearchPath = "line_items/autocomplete_product_code?term={term}";
    public const string DefaultProductDetailsPath = "product_data?id={id}&description_type=false&description_change=false";
    public const int DefaultRequestTimeoutSeconds = 8;

    public string BaseUrl { get; set; } = DefaultBaseUrl;
    public string LoginPath { get; set; } = DefaultLoginPath;
    public string CatalogPath { get; set; } = DefaultCatalogPath;
    public string SearchPath { get; set; } = DefaultSearchPath;
    public string ProductDetailsPath { get; set; } = DefaultProductDetailsPath;
    public int RequestTimeoutSeconds { get; set; } = DefaultRequestTimeoutSeconds;

    [JsonIgnore]
    public Uri BaseUri => new(BaseUrl, UriKind.Absolute);
    [JsonIgnore]
    public Uri LoginUri => new(BaseUri, LoginPath);
    [JsonIgnore]
    public Uri CatalogUri => new(BaseUri, CatalogPath);

    public Uri SearchProductsUri(string term) => new(CatalogUri,
        SearchPath.Replace("{term}", Uri.EscapeDataString(term), StringComparison.Ordinal));

    public Uri ProductDetailsUri(string uuid) => new(CatalogUri,
        ProductDetailsPath.Replace("{id}", Uri.EscapeDataString(uuid), StringComparison.Ordinal));

    public VeluxSettingsModel CreateValidatedCopy()
    {
        string baseUrl = BaseUrl?.Trim() ?? string.Empty;
        if (!baseUrl.Contains("://", StringComparison.Ordinal)) baseUrl = "https://" + baseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var origin) || origin.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(origin.Host) || origin.UserInfo.Length > 0 || origin.AbsolutePath != "/" ||
            origin.Query.Length > 0 || origin.Fragment.Length > 0)
            throw new InvalidOperationException("Inserisci il dominio HTTPS VELUX, per esempio https://appvelux.devdemo.it, senza percorsi o credenziali.");
        if (RequestTimeoutSeconds is < 1 or > 120)
            throw new InvalidOperationException("Il tempo massimo di attesa VELUX deve essere compreso tra 1 e 120 secondi.");

        origin = new Uri(origin.GetLeftPart(UriPartial.Authority) + "/");
        string login = NormalizePath(origin, origin, LoginPath, "login");
        string catalog = NormalizePath(origin, origin, CatalogPath, "catalogo", catalog: true);
        var catalogUri = new Uri(origin, catalog);
        string search = NormalizePath(origin, catalogUri, SearchPath, "ricerca", placeholder: "{term}");
        string details = NormalizePath(origin, catalogUri, ProductDetailsPath, "dettagli prodotto", placeholder: "{id}");
        return this with { BaseUrl = origin.AbsoluteUri, LoginPath = login, CatalogPath = catalog, SearchPath = search, ProductDetailsPath = details };
    }

    private static string NormalizePath(Uri origin, Uri parent, string? configured, string label,
        bool catalog = false, string? placeholder = null)
    {
        string path = configured?.Trim() ?? string.Empty;
        if (path.Length == 0 || path.Contains('\\') ||
            (placeholder != null && !path.Contains(placeholder, StringComparison.Ordinal)))
            throw new InvalidOperationException(placeholder == null
                ? $"Inserisci un percorso valido per {label} VELUX."
                : $"Il percorso {label} VELUX deve contenere il segnaposto {placeholder}.");

        const string marker = "__velux_query_value__";
        string probe = placeholder == null ? path : path.Replace(placeholder, marker, StringComparison.Ordinal);
        if (!Uri.TryCreate(parent, probe, out var resolved) || resolved.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(resolved.Host, origin.Host, StringComparison.OrdinalIgnoreCase) || resolved.Port != origin.Port ||
            resolved.UserInfo.Length > 0 || resolved.Fragment.Length > 0 || (catalog && resolved.Query.Length > 0))
            throw new InvalidOperationException($"Il percorso {label} VELUX deve usare lo stesso dominio HTTPS configurato, senza frammenti.");

        if (catalog)
            return resolved.AbsolutePath.TrimEnd('/') + "/";
        if (path.StartsWith('/') || Uri.TryCreate(path, UriKind.Absolute, out _))
            return placeholder == null ? resolved.PathAndQuery : resolved.PathAndQuery.Replace(marker, placeholder, StringComparison.Ordinal);
        return path;
    }
}
