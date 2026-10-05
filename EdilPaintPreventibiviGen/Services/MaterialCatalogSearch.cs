using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public static class MaterialCatalogSearch
{
    public static IReadOnlyList<CatalogMaterialOption> Find(IEnumerable<Item> catalog, string? query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string text = query?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return [];

        var matches = new List<CatalogMaterialOption>();
        foreach (var item in catalog)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!item.IsCompanyMaterial && (item.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || item.Description.Contains(text, StringComparison.OrdinalIgnoreCase)))
                matches.Add(new CatalogMaterialOption(item));
        }
        return matches;
    }
}
