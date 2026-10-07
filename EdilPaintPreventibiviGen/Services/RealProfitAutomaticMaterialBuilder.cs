using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed record AutomaticMaterialBuildResult(IReadOnlyList<CompanyMaterialCost> Materials,
    IReadOnlyList<string> Notices, bool HasWarnings);

public static class RealProfitAutomaticMaterialBuilder
{
    public static Item? ResolveCatalogItem(IEnumerable<Item> catalog, int? id, string? name, bool trustCatalogIds = true)
    {
        var items = catalog.ToArray();
        if (trustCatalogIds && id > 0)
        {
            var byId = items.Where(item => item.PersistentId == id).Take(2).ToArray();
            if (byId.Length > 0) return byId.Length == 1 ? byId[0] : null;
        }
        string normalizedName = name?.Trim() ?? "";
        if (normalizedName.Length == 0) return null;
        var byName = items.Where(item => string.Equals(item.Name.Trim(), normalizedName,
            StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return byName.Length == 1 ? byName[0] : null;
    }

    public static AutomaticMaterialSettings RebindSettings(AutomaticMaterialSettings settings,
        IReadOnlyList<Item> laborCatalog, IReadOnlyList<Item> companyMaterials, bool trustCatalogIds = true)
    {
        var copy = settings.CreateValidatedCopy();
        foreach (var rule in copy.WindowMaterialRules)
        {
            var labor = ResolveCatalogItem(laborCatalog, rule.LaborCatalogId, rule.LaborName, trustCatalogIds);
            var material = ResolveCatalogItem(companyMaterials, rule.MaterialCatalogId, rule.MaterialName, trustCatalogIds);
            rule.LaborCatalogId = labor?.PersistentId > 0 ? labor.PersistentId : null;
            rule.MaterialCatalogId = material?.PersistentId > 0 ? material.PersistentId : null;
            if (labor != null) rule.LaborName = labor.Name;
            if (material != null) rule.MaterialName = material.Name;
        }
        return copy;
    }

    public static AutomaticMaterialBuildResult Build(QuoteHistoryEntry quote, AutomaticMaterialSettings settings,
        IReadOnlyList<Item> laborCatalog, IReadOnlyList<Item> companyMaterials, bool trustCatalogIds = true,
        bool enableWindowAutomations = true)
    {
        ArgumentNullException.ThrowIfNull(quote);
        var catalog = companyMaterials.Where(x => x.IsCompanyMaterial).ToArray();
        var quoteLabors = quote.Labors.Where(x => x.Quantity > 0)
            .Select(x => (Line: x, Item: ResolveQuoteItem(laborCatalog, x, trustCatalogIds))).ToArray();
        var prepared = new List<AutomaticWindowMaterialRule>();
        var notices = new List<string>();
        foreach (var pair in settings.WindowMaterialRules.Select((rule, index) => (rule, index)))
        {
            var rule = pair.rule;
            if (rule.IsWindowAutomation && !enableWindowAutomations) continue;
            if (!rule.Enabled)
            {
                bool applies = quoteLabors.Any(x => string.Equals(x.Line.Name.Trim(), rule.LaborName.Trim(),
                    StringComparison.OrdinalIgnoreCase));
                if (applies)
                    notices.AddRange((settings.ResolutionWarnings ?? []).Where(warning =>
                        warning.Contains($"\"{rule.LaborName}\"", StringComparison.Ordinal) &&
                        warning.Contains($"\"{rule.MaterialName}\"", StringComparison.Ordinal)));
                continue;
            }
            var labor = ResolveCatalogItem(laborCatalog, rule.LaborCatalogId, rule.LaborName, trustCatalogIds);
            bool verifiedMatch = labor != null && quoteLabors.Any(x => x.Item != null &&
                (labor.PersistentId > 0 ? x.Item.PersistentId == labor.PersistentId
                    : string.Equals(x.Item.Name.Trim(), labor.Name.Trim(), StringComparison.OrdinalIgnoreCase)));
            if (!verifiedMatch)
            {
                bool unresolvedMatch = quoteLabors.Any(x => (labor == null || x.Item == null) &&
                    (string.Equals(x.Line.Name.Trim(), rule.LaborName.Trim(), StringComparison.OrdinalIgnoreCase) ||
                     (trustCatalogIds && rule.LaborCatalogId > 0 && x.Line.PersistentId == rule.LaborCatalogId)));
                if (unresolvedMatch)
                    notices.Add($"La lavorazione \"{rule.LaborName}\" dell'intervento non è riconosciuta in modo univoco nel catalogo. Verifica la regola nelle impostazioni.");
                continue;
            }
            var material = ResolveCatalogItem(catalog, rule.MaterialCatalogId, rule.MaterialName, trustCatalogIds);
            if (labor == null || material == null || !double.IsFinite(material.UnitPrice) || material.UnitPrice < 0)
            {
                notices.Add($"Regola {pair.index + 1}: {(labor == null ? $"lavorazione \"{rule.LaborName}\"" : $"materiale \"{rule.MaterialName}\"")} " +
                    "non riconosciuto in modo univoco nel catalogo. Verifica la regola nelle impostazioni; il relativo costo automatico non viene applicato.");
                continue;
            }
            prepared.Add(new AutomaticWindowMaterialRule
            {
                RuleId = $"regola-{pair.index + 1}", Enabled = true, IsWindowAutomation = rule.IsWindowAutomation,
                LaborCatalogItemId = labor.PersistentId, LaborNameSnapshot = labor.Name,
                MaterialCatalogItemId = material.PersistentId, MaterialNameSnapshot = material.Name,
                Mode = rule.CalculationMode, Parameter = rule.QuantityParameter
            });
        }

        var calculation = AutomaticWindowMaterialCalculator.Calculate(new()
        {
            WindowProducts = quote.Materials.Select(x => new AutomaticWindowProductLine(x.Name, x.Quantity, x.UnitOfMeasure)).ToArray(),
            Labors = quoteLabors
                .Where(x => x.Item != null).Select(x => new AutomaticWindowLaborLine(
                    x.Item!.PersistentId, x.Item.Name, x.Line.Quantity, x.Line.UnitOfMeasure)).ToArray(),
            ExistingQuoteMaterials = quote.Materials.Select(x => (Line: x, Item: ResolveQuoteItem(catalog, x, trustCatalogIds)))
                .Where(x => x.Item != null).Select(x => new AutomaticQuoteMaterialLine(
                    x.Item!.PersistentId, x.Item.Name, x.Line.Quantity, x.Line.UnitOfMeasure)).ToArray(),
            Rules = prepared, WindowPrefixes = settings.WindowProductPrefixes,
            MaterialCatalog = catalog.Select(x => new AutomaticMaterialCatalogItem(x.PersistentId, x.Name, x.UnitOfMeasure)).ToArray()
        });
        notices.AddRange(calculation.Issues.Select(x => x.Message));
        var materials = new List<CompanyMaterialCost>();
        foreach (var plan in calculation.Materials.Where(x => x.QuantityToAdd > 0))
        {
            var item = ResolveCatalogItem(catalog, plan.MaterialCatalogItemId, plan.MaterialName);
            if (item == null || !QuantityValue.IsValid(plan.QuantityToAdd))
            {
                notices.Add($"Il costo di \"{plan.MaterialName}\" non può essere applicato automaticamente: verifica materiale e quantità.");
                continue;
            }
            materials.Add(new CompanyMaterialCost
            {
                Name = item.Name, Quantity = plan.QuantityToAdd, UnitOfMeasure = item.UnitOfMeasure,
                UnitCost = item.UnitPrice, Source = "Automatico"
            });
        }
        bool hasWarnings = notices.Count > 0;
        notices.AddRange(materials.Select(x => $"• {x.Name}: {x.Quantity} {x.UnitOfMeasure}, costo catalogo {x.UnitCost:N2} €/unità."));
        return new(materials, notices.Distinct().ToArray(), hasWarnings);
    }

    private static Item? ResolveQuoteItem(IReadOnlyList<Item> catalog, Item line, bool trustIds)
    {
        // A quote imported from an older catalogue may carry an ID now used by
        // another item. A unique exact name gives a verifiable remapping. A
        // genuine rename still keeps the ID when the old name no longer exists.
        var named = ResolveCatalogItem(catalog, null, line.Name, trustCatalogIds: false);
        if (named != null) return named;
        return ResolveCatalogItem(catalog, line.PersistentId, line.Name, trustIds);
    }
}
