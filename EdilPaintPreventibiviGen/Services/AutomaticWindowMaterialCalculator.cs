using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public static class AutomaticWindowMaterialCalculator
{
    public static AutomaticWindowMaterialCalculationResult Calculate(
        AutomaticWindowMaterialCalculationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        IReadOnlyCollection<AutomaticWindowProductLine> products =
            input.WindowProducts ?? Array.Empty<AutomaticWindowProductLine>();
        IReadOnlyCollection<AutomaticWindowLaborLine> labors =
            input.Labors ?? Array.Empty<AutomaticWindowLaborLine>();
        IReadOnlyCollection<AutomaticQuoteMaterialLine> existingMaterials =
            input.ExistingQuoteMaterials ?? Array.Empty<AutomaticQuoteMaterialLine>();
        IReadOnlyCollection<AutomaticWindowMaterialRule> rules =
            input.Rules ?? Array.Empty<AutomaticWindowMaterialRule>();
        IReadOnlyCollection<string> prefixes =
            input.WindowPrefixes ?? Array.Empty<string>();
        IReadOnlyCollection<AutomaticMaterialCatalogItem> catalog =
            input.MaterialCatalog ?? Array.Empty<AutomaticMaterialCatalogItem>();

        var issues = new List<AutomaticWindowMaterialIssue>();
        List<PreparedRule> activeRules = PrepareActiveRules(rules, labors, catalog, issues);
        if (activeRules.Count == 0)
        {
            return new AutomaticWindowMaterialCalculationResult
            {
                Issues = issues
            };
        }

        bool requiresWindowRecognition = activeRules.Any(rule => rule.IsWindowAutomation);
        var windows = new List<AutomaticRecognizedWindowGroup>();
        if (requiresWindowRecognition &&
            !TryRecognizeWindows(products, prefixes, issues, out windows))
        {
            return new AutomaticWindowMaterialCalculationResult
            {
                Issues = issues
            };
        }

        if (requiresWindowRecognition && windows.Count == 0)
        {
            issues.Add(new AutomaticWindowMaterialIssue(
                AutomaticWindowMaterialIssueCode.NoRecognizedWindows,
                "È presente almeno una regola attiva, ma non è stata riconosciuta alcuna finestra."));
        }

        var calculations = new List<AutomaticWindowMaterialRuleCalculation>();
        foreach (PreparedRule preparedRule in activeRules)
        {
            if (!TryCalculateRule(preparedRule, windows, issues, out AutomaticWindowMaterialRuleCalculation? calculation))
            {
                return new AutomaticWindowMaterialCalculationResult
                {
                    RecognizedWindows = windows,
                    Issues = issues
                };
            }

            calculations.Add(calculation!);
        }

        List<AutomaticWindowMaterialPlanLine> materials = Consolidate(
            calculations,
            activeRules,
            existingMaterials,
            issues);

        return new AutomaticWindowMaterialCalculationResult
        {
            RecognizedWindows = windows,
            RuleCalculations = calculations,
            Materials = materials,
            Issues = issues
        };
    }

    private static List<PreparedRule> PrepareActiveRules(
        IEnumerable<AutomaticWindowMaterialRule> rules,
        IReadOnlyCollection<AutomaticWindowLaborLine> labors,
        IReadOnlyCollection<AutomaticMaterialCatalogItem> catalog,
        ICollection<AutomaticWindowMaterialIssue> issues)
    {
        var preparedRules = new List<PreparedRule>();
        var ruleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var signatures = new HashSet<RuleSignature>();
        int ruleIndex = 0;

        foreach (AutomaticWindowMaterialRule? rule in rules)
        {
            ruleIndex++;
            if (rule == null || !rule.Enabled)
                continue;

            string ruleId = string.IsNullOrWhiteSpace(rule.RuleId)
                ? $"regola-{ruleIndex}"
                : rule.RuleId.Trim();

            if (!ruleIds.Add(ruleId))
            {
                issues.Add(new AutomaticWindowMaterialIssue(
                    AutomaticWindowMaterialIssueCode.DuplicateRule,
                    $"L'identificativo della regola \"{ruleId}\" è duplicato; la seconda regola è stata ignorata.",
                    ruleId));
                continue;
            }

            string laborSnapshot = CleanDisplayName(rule.LaborNameSnapshot);
            if (rule.LaborCatalogItemId <= 0 && laborSnapshot.Length == 0)
            {
                issues.Add(new AutomaticWindowMaterialIssue(
                    AutomaticWindowMaterialIssueCode.InvalidRule,
                    "La regola non identifica una lavorazione tramite ID o nome.",
                    ruleId));
                continue;
            }

            if (!TryNormalizeMode(rule.Mode, out string normalizedMode))
            {
                issues.Add(new AutomaticWindowMaterialIssue(
                    AutomaticWindowMaterialIssueCode.UnsupportedMode,
                    $"La modalità \"{rule.Mode}\" della regola non è supportata.",
                    ruleId));
                continue;
            }

            if (!rule.IsWindowAutomation)
                normalizedMode = AutomaticWindowMaterialModes.FixedPerWindow;

            if (rule.Parameter <= 0)
            {
                issues.Add(new AutomaticWindowMaterialIssue(
                    AutomaticWindowMaterialIssueCode.InvalidRule,
                    "Il parametro della regola deve essere maggiore di zero.",
                    ruleId));
                continue;
            }

            long laborQuantity = GetMatchingLaborQuantity(rule, laborSnapshot, labors);
            if (laborQuantity <= 0)
                continue;

            ResolvedMaterial material = ResolveMaterial(rule, catalog);
            if (!material.IsValid)
            {
                issues.Add(new AutomaticWindowMaterialIssue(
                    AutomaticWindowMaterialIssueCode.InvalidRule,
                    "La regola non identifica un materiale tramite ID o nome.",
                    ruleId));
                continue;
            }

            var signature = new RuleSignature(
                CreateLaborKey(rule.LaborCatalogItemId, laborSnapshot),
                material.Key,
                rule.IsWindowAutomation,
                normalizedMode,
                rule.Parameter);

            if (!signatures.Add(signature))
            {
                issues.Add(new AutomaticWindowMaterialIssue(
                    AutomaticWindowMaterialIssueCode.DuplicateRule,
                    $"La regola \"{ruleId}\" duplica una regola già attiva ed è stata ignorata.",
                    ruleId));
                continue;
            }

            AddMaterialResolutionIssue(material, ruleId, issues);
            preparedRules.Add(new PreparedRule(
                ruleId,
                rule.LaborCatalogItemId,
                laborSnapshot,
                rule.IsWindowAutomation,
                laborQuantity,
                normalizedMode,
                rule.Parameter,
                material));
        }

        return preparedRules;
    }

    private static long GetMatchingLaborQuantity(
        AutomaticWindowMaterialRule rule,
        string laborSnapshot,
        IEnumerable<AutomaticWindowLaborLine> labors)
    {
        long totalQuantity = 0;
        string normalizedSnapshot = NormalizeName(laborSnapshot);
        foreach (AutomaticWindowLaborLine labor in labors)
        {
            if (labor.Quantity <= 0)
                continue;

            if (rule.LaborCatalogItemId > 0 && labor.CatalogItemId > 0)
            {
                if (rule.LaborCatalogItemId == labor.CatalogItemId)
                    totalQuantity = checked(totalQuantity + labor.Quantity);

                continue;
            }

            if (normalizedSnapshot.Length > 0 &&
                string.Equals(NormalizeName(labor.Name), normalizedSnapshot, StringComparison.Ordinal))
            {
                totalQuantity = checked(totalQuantity + labor.Quantity);
            }
        }

        return totalQuantity;
    }

    private static bool TryRecognizeWindows(
        IEnumerable<AutomaticWindowProductLine> products,
        IReadOnlyCollection<string> prefixes,
        ICollection<AutomaticWindowMaterialIssue> issues,
        out List<AutomaticRecognizedWindowGroup> windows)
    {
        windows = [];
        string[] normalizedPrefixes = prefixes
            .Select(CleanDisplayName)
            .Where(prefix => prefix.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var quantitiesBySize = new Dictionary<WindowSize, int>();

        foreach (AutomaticWindowProductLine product in products)
        {
            if (product.Quantity <= 0)
                continue;

            string productName = product.Name?.TrimStart() ?? string.Empty;
            if (!StartsWithPrefix(productName, normalizedPrefixes))
                continue;

            if (!WindowMaterialCalculator.TryGetWindowSize(productName, normalizedPrefixes, out WindowSize size))
            {
                issues.Add(new AutomaticWindowMaterialIssue(
                    AutomaticWindowMaterialIssueCode.UnrecognizedWindowProduct,
                    $"La misura della finestra \"{product.Name}\" non è stata riconosciuta.",
                    ItemName: product.Name ?? string.Empty));
                continue;
            }

            try
            {
                quantitiesBySize[size] = checked(quantitiesBySize.GetValueOrDefault(size) + product.Quantity);
            }
            catch (OverflowException)
            {
                issues.Add(new AutomaticWindowMaterialIssue(
                    AutomaticWindowMaterialIssueCode.QuantityOverflow,
                    $"La quantità totale delle finestre {size} supera il limite supportato.",
                    ItemName: product.Name ?? string.Empty));
                return false;
            }
        }

        windows = quantitiesBySize
            .OrderBy(pair => pair.Key.WidthCentimeters)
            .ThenBy(pair => pair.Key.HeightCentimeters)
            .Select(pair => new AutomaticRecognizedWindowGroup(pair.Key, pair.Value))
            .ToList();
        return true;
    }

    private static bool TryCalculateRule(
        PreparedRule rule,
        IReadOnlyCollection<AutomaticRecognizedWindowGroup> windows,
        ICollection<AutomaticWindowMaterialIssue> issues,
        out AutomaticWindowMaterialRuleCalculation? calculation)
    {
        calculation = null;
        var details = new List<AutomaticWindowRuleSizeCalculation>();
        long grossRequired = 0;

        try
        {
            if (!rule.IsWindowAutomation)
            {
                decimal roundedDecimal = decimal.Ceiling(
                    checked(rule.Parameter * rule.LaborQuantity));
                if (roundedDecimal > long.MaxValue)
                    throw new OverflowException();

                grossRequired = decimal.ToInt64(roundedDecimal);
            }
            else
            {
                long remainingLaborQuantity = rule.LaborQuantity;
                foreach (AutomaticRecognizedWindowGroup window in windows)
                {
                    if (remainingLaborQuantity <= 0)
                        break;

                    int applicableWindowQuantity = (int)Math.Min(
                        window.WindowQuantity,
                        remainingLaborQuantity);
                    decimal rawQuantity = rule.Mode == AutomaticWindowMaterialModes.Perimeter
                        ? (2m * (window.Size.WidthCentimeters + window.Size.HeightCentimeters) / 100m) * rule.Parameter
                        : rule.Parameter;
                    decimal roundedDecimal = decimal.Ceiling(rawQuantity);
                    if (roundedDecimal > long.MaxValue)
                        throw new OverflowException();

                    long roundedPerWindow = decimal.ToInt64(roundedDecimal);
                    long requiredQuantity = checked(roundedPerWindow * applicableWindowQuantity);
                    grossRequired = checked(grossRequired + requiredQuantity);
                    details.Add(new AutomaticWindowRuleSizeCalculation(
                        window.Size,
                        applicableWindowQuantity,
                        rawQuantity,
                        roundedPerWindow,
                        requiredQuantity));
                    remainingLaborQuantity -= applicableWindowQuantity;
                }

                if (remainingLaborQuantity > 0)
                {
                    issues.Add(new AutomaticWindowMaterialIssue(
                        AutomaticWindowMaterialIssueCode.InsufficientRecognizedWindows,
                        $"La lavorazione \"{rule.LaborName}\" ha quantità {rule.LaborQuantity}, " +
                        $"ma sono state riconosciute solo {windows.Sum(window => (long)window.WindowQuantity)} finestre. " +
                        "Il materiale automatico è stato calcolato soltanto per le finestre riconosciute.",
                        rule.RuleId,
                        rule.LaborName));
                }
            }
        }
        catch (OverflowException)
        {
            issues.Add(new AutomaticWindowMaterialIssue(
                AutomaticWindowMaterialIssueCode.QuantityOverflow,
                $"Il fabbisogno calcolato dalla regola \"{rule.RuleId}\" supera il limite supportato.",
                rule.RuleId));
            return false;
        }

        calculation = new AutomaticWindowMaterialRuleCalculation(
            rule.RuleId,
            rule.LaborCatalogItemId,
            rule.LaborName,
            rule.IsWindowAutomation,
            rule.LaborQuantity,
            rule.Material.Key,
            rule.Material.CatalogItemId,
            rule.Material.DisplayName,
            rule.Material.Status,
            rule.Mode,
            rule.Parameter,
            grossRequired,
            details);
        return true;
    }

    private static List<AutomaticWindowMaterialPlanLine> Consolidate(
        IEnumerable<AutomaticWindowMaterialRuleCalculation> calculations,
        IReadOnlyCollection<PreparedRule> preparedRules,
        IEnumerable<AutomaticQuoteMaterialLine> existingMaterials,
        ICollection<AutomaticWindowMaterialIssue> issues)
    {
        IReadOnlyDictionary<string, PreparedRule> preparedById = preparedRules
            .GroupBy(rule => rule.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<AutomaticMaterialKey, MaterialAggregate>();

        foreach (AutomaticWindowMaterialRuleCalculation calculation in calculations)
        {
            if (!groups.TryGetValue(calculation.MaterialKey, out MaterialAggregate? aggregate))
            {
                PreparedRule prepared = preparedById[calculation.RuleId];
                aggregate = new MaterialAggregate(
                    calculation.MaterialKey,
                    calculation.MaterialCatalogItemId,
                    calculation.MaterialName,
                    calculation.MaterialResolution,
                    prepared.Material.AllowLegacyNameMatch);
                groups.Add(calculation.MaterialKey, aggregate);
            }

            try
            {
                aggregate.GrossRequiredQuantity = checked(
                    aggregate.GrossRequiredQuantity + calculation.GrossRequiredQuantity);
            }
            catch (OverflowException)
            {
                aggregate.HasOverflow = true;
                issues.Add(new AutomaticWindowMaterialIssue(
                    AutomaticWindowMaterialIssueCode.QuantityOverflow,
                    $"Il fabbisogno aggregato del materiale \"{aggregate.MaterialName}\" supera il limite supportato.",
                    calculation.RuleId,
                    aggregate.MaterialName));
            }

            aggregate.RuleIds.Add(calculation.RuleId);
            aggregate.Details.AddRange(calculation.Details);
            if (preparedById.TryGetValue(calculation.RuleId, out PreparedRule? sourceRule))
            {
                aggregate.Aliases.UnionWith(sourceRule.Material.Aliases);
                aggregate.AllowLegacyNameMatch &= sourceRule.Material.AllowLegacyNameMatch;
            }
            aggregate.Resolution = MoreSevere(aggregate.Resolution, calculation.MaterialResolution);
        }

        var warnedAmbiguousNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (AutomaticQuoteMaterialLine existing in existingMaterials)
        {
            if (existing.Quantity <= 0)
                continue;

            MaterialAggregate? match = null;
            string normalizedName = NormalizeName(existing.Name);
            if (existing.CatalogItemId > 0)
            {
                groups.TryGetValue(
                    AutomaticMaterialKey.FromCatalogItemId(existing.CatalogItemId),
                    out match);

                if (match == null && normalizedName.Length > 0)
                {
                    List<MaterialAggregate> legacyMatches = groups.Values
                        .Where(group =>
                            !group.MaterialKey.HasCatalogItemId &&
                            group.AllowLegacyNameMatch &&
                            group.Aliases.Contains(normalizedName))
                        .ToList();
                    if (legacyMatches.Count == 1)
                        match = legacyMatches[0];
                    else if (legacyMatches.Count > 1)
                        AddAmbiguousExistingIssue(existing, normalizedName, warnedAmbiguousNames, issues);
                }
            }
            else if (normalizedName.Length > 0)
            {
                List<MaterialAggregate> allNameMatches = groups.Values
                    .Where(group => group.Aliases.Contains(normalizedName))
                    .ToList();
                List<MaterialAggregate> safeNameMatches = allNameMatches
                    .Where(group => group.AllowLegacyNameMatch)
                    .ToList();

                if (allNameMatches.Count == 1 && safeNameMatches.Count == 1)
                    match = safeNameMatches[0];
                else if (allNameMatches.Count > 0)
                    AddAmbiguousExistingIssue(existing, normalizedName, warnedAmbiguousNames, issues);
            }

            if (match == null)
                continue;

            try
            {
                match.AlreadyQuotedQuantity = checked(match.AlreadyQuotedQuantity + existing.Quantity);
            }
            catch (OverflowException)
            {
                match.HasOverflow = true;
                issues.Add(new AutomaticWindowMaterialIssue(
                    AutomaticWindowMaterialIssueCode.QuantityOverflow,
                    $"La quantità già a preventivo del materiale \"{match.MaterialName}\" supera il limite supportato.",
                    ItemName: match.MaterialName));
            }
        }

        if (groups.Values.Any(group => group.HasOverflow))
            return [];

        return groups.Values
            .Where(group => group.GrossRequiredQuantity > 0)
            .OrderBy(group => group.MaterialName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.MaterialKey.ToString(), StringComparer.Ordinal)
            .Select(group => new AutomaticWindowMaterialPlanLine(
                group.MaterialKey,
                group.MaterialCatalogItemId,
                group.MaterialName,
                group.Resolution,
                group.GrossRequiredQuantity,
                group.AlreadyQuotedQuantity,
                Math.Max(0, group.GrossRequiredQuantity - group.AlreadyQuotedQuantity),
                group.RuleIds.ToArray(),
                group.Details.ToArray()))
            .ToList();
    }

    private static void AddAmbiguousExistingIssue(
        AutomaticQuoteMaterialLine existing,
        string normalizedName,
        ISet<string> warnedNames,
        ICollection<AutomaticWindowMaterialIssue> issues)
    {
        if (!warnedNames.Add(normalizedName))
            return;

        issues.Add(new AutomaticWindowMaterialIssue(
            AutomaticWindowMaterialIssueCode.AmbiguousExistingMaterial,
            $"Il materiale legacy \"{existing.Name}\" già a preventivo non identifica una sola regola e non è stato sottratto.",
            ItemName: existing.Name ?? string.Empty));
    }

    private static ResolvedMaterial ResolveMaterial(
        AutomaticWindowMaterialRule rule,
        IReadOnlyCollection<AutomaticMaterialCatalogItem> catalog)
    {
        string snapshot = CleanDisplayName(rule.MaterialNameSnapshot);
        string normalizedSnapshot = NormalizeName(snapshot);

        if (rule.MaterialCatalogItemId > 0)
        {
            AutomaticMaterialCatalogItem[] idMatches = catalog
                .Where(item => item.CatalogItemId == rule.MaterialCatalogItemId)
                .GroupBy(item => NormalizeName(item.Name), StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            var aliases = CreateAliasSet(snapshot, idMatches.Select(item => item.Name));
            AutomaticMaterialKey key = AutomaticMaterialKey.FromCatalogItemId(rule.MaterialCatalogItemId);

            if (idMatches.Length == 1)
            {
                string catalogName = CleanDisplayName(idMatches[0].Name);
                return new ResolvedMaterial(
                    true,
                    key,
                    rule.MaterialCatalogItemId,
                    catalogName.Length > 0 ? catalogName : snapshot,
                    AutomaticMaterialResolutionStatus.ResolvedById,
                    aliases,
                    true);
            }

            if (idMatches.Length == 0)
            {
                return new ResolvedMaterial(
                    snapshot.Length > 0,
                    key,
                    rule.MaterialCatalogItemId,
                    snapshot,
                    AutomaticMaterialResolutionStatus.MissingFromCatalog,
                    aliases,
                    snapshot.Length > 0);
            }

            return new ResolvedMaterial(
                snapshot.Length > 0,
                key,
                rule.MaterialCatalogItemId,
                snapshot,
                AutomaticMaterialResolutionStatus.AmbiguousName,
                aliases,
                false);
        }

        if (normalizedSnapshot.Length == 0)
            return ResolvedMaterial.Invalid;

        IndexedCatalogMaterial[] nameMatches = catalog
            .Select((item, index) => new IndexedCatalogMaterial(item, index))
            .Where(candidate => string.Equals(
                NormalizeName(candidate.Item.Name),
                normalizedSnapshot,
                StringComparison.Ordinal))
            .GroupBy(candidate => candidate.Identity)
            .Select(group => group.First())
            .ToArray();

        if (nameMatches.Length == 1)
        {
            AutomaticMaterialCatalogItem item = nameMatches[0].Item;
            int resolvedId = item.CatalogItemId > 0 ? item.CatalogItemId : 0;
            AutomaticMaterialKey key = resolvedId > 0
                ? AutomaticMaterialKey.FromCatalogItemId(resolvedId)
                : AutomaticMaterialKey.FromLegacyName(normalizedSnapshot);
            return new ResolvedMaterial(
                true,
                key,
                resolvedId,
                CleanDisplayName(item.Name),
                AutomaticMaterialResolutionStatus.ResolvedByUniqueName,
                CreateAliasSet(snapshot, [item.Name]),
                true);
        }

        AutomaticMaterialKey legacyKey = AutomaticMaterialKey.FromLegacyName(normalizedSnapshot);
        if (nameMatches.Length == 0)
        {
            return new ResolvedMaterial(
                true,
                legacyKey,
                0,
                snapshot,
                AutomaticMaterialResolutionStatus.MissingFromCatalog,
                CreateAliasSet(snapshot, []),
                true);
        }

        return new ResolvedMaterial(
            true,
            legacyKey,
            0,
            snapshot,
            AutomaticMaterialResolutionStatus.AmbiguousName,
            CreateAliasSet(snapshot, nameMatches.Select(match => match.Item.Name)),
            false);
    }

    private static void AddMaterialResolutionIssue(
        ResolvedMaterial material,
        string ruleId,
        ICollection<AutomaticWindowMaterialIssue> issues)
    {
        if (material.Status == AutomaticMaterialResolutionStatus.MissingFromCatalog)
        {
            issues.Add(new AutomaticWindowMaterialIssue(
                AutomaticWindowMaterialIssueCode.MissingCatalogMaterial,
                $"Il materiale \"{material.DisplayName}\" della regola non è presente nel catalogo.",
                ruleId,
                material.DisplayName));
        }
        else if (material.Status == AutomaticMaterialResolutionStatus.AmbiguousName)
        {
            issues.Add(new AutomaticWindowMaterialIssue(
                AutomaticWindowMaterialIssueCode.AmbiguousCatalogMaterial,
                $"Il materiale \"{material.DisplayName}\" corrisponde a più elementi del catalogo e non è stato scelto automaticamente.",
                ruleId,
                material.DisplayName));
        }
    }

    private static AutomaticMaterialResolutionStatus MoreSevere(
        AutomaticMaterialResolutionStatus left,
        AutomaticMaterialResolutionStatus right) =>
        Severity(right) > Severity(left) ? right : left;

    private static int Severity(AutomaticMaterialResolutionStatus status) => status switch
    {
        AutomaticMaterialResolutionStatus.ResolvedById => 0,
        AutomaticMaterialResolutionStatus.ResolvedByUniqueName => 1,
        AutomaticMaterialResolutionStatus.MissingFromCatalog => 2,
        AutomaticMaterialResolutionStatus.AmbiguousName => 3,
        _ => 3
    };

    private static string CreateLaborKey(int catalogItemId, string name) => catalogItemId > 0
        ? $"id:{catalogItemId}"
        : $"name:{NormalizeName(name)}";

    private static bool TryNormalizeMode(string? mode, out string normalizedMode)
    {
        string value = mode?.Trim() ?? string.Empty;
        if (string.Equals(value, AutomaticWindowMaterialModes.Perimeter, StringComparison.OrdinalIgnoreCase))
        {
            normalizedMode = AutomaticWindowMaterialModes.Perimeter;
            return true;
        }

        if (string.Equals(value, AutomaticWindowMaterialModes.FixedPerWindow, StringComparison.OrdinalIgnoreCase))
        {
            normalizedMode = AutomaticWindowMaterialModes.FixedPerWindow;
            return true;
        }

        normalizedMode = string.Empty;
        return false;
    }

    private static bool StartsWithPrefix(string productName, IEnumerable<string> prefixes) =>
        prefixes.Any(prefix => productName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static string CleanDisplayName(string? value) => value?.Trim() ?? string.Empty;

    private static string NormalizeName(string? value) => CleanDisplayName(value).ToUpperInvariant();

    private static HashSet<string> CreateAliasSet(string snapshot, IEnumerable<string> otherNames)
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        string normalizedSnapshot = NormalizeName(snapshot);
        if (normalizedSnapshot.Length > 0)
            aliases.Add(normalizedSnapshot);
        foreach (string name in otherNames)
        {
            string normalized = NormalizeName(name);
            if (normalized.Length > 0)
                aliases.Add(normalized);
        }

        return aliases;
    }

    private sealed record PreparedRule(
        string RuleId,
        int LaborCatalogItemId,
        string LaborName,
        bool IsWindowAutomation,
        long LaborQuantity,
        string Mode,
        decimal Parameter,
        ResolvedMaterial Material);

    private sealed record ResolvedMaterial(
        bool IsValid,
        AutomaticMaterialKey Key,
        int CatalogItemId,
        string DisplayName,
        AutomaticMaterialResolutionStatus Status,
        HashSet<string> Aliases,
        bool AllowLegacyNameMatch)
    {
        public static ResolvedMaterial Invalid { get; } = new(
            false,
            default,
            0,
            string.Empty,
            AutomaticMaterialResolutionStatus.MissingFromCatalog,
            new HashSet<string>(StringComparer.Ordinal),
            false);
    }

    private sealed record RuleSignature(
        string LaborKey,
        AutomaticMaterialKey MaterialKey,
        bool IsWindowAutomation,
        string Mode,
        decimal Parameter);

    private sealed record IndexedCatalogMaterial(AutomaticMaterialCatalogItem Item, int Index)
    {
        public string Identity => Item.CatalogItemId > 0
            ? $"id:{Item.CatalogItemId}"
            : $"legacy:{Index}";
    }

    private sealed class MaterialAggregate
    {
        public MaterialAggregate(
            AutomaticMaterialKey materialKey,
            int materialCatalogItemId,
            string materialName,
            AutomaticMaterialResolutionStatus resolution,
            bool allowLegacyNameMatch)
        {
            MaterialKey = materialKey;
            MaterialCatalogItemId = materialCatalogItemId;
            MaterialName = materialName;
            Resolution = resolution;
            AllowLegacyNameMatch = allowLegacyNameMatch;
        }

        public AutomaticMaterialKey MaterialKey { get; }
        public int MaterialCatalogItemId { get; }
        public string MaterialName { get; }
        public AutomaticMaterialResolutionStatus Resolution { get; set; }
        public bool AllowLegacyNameMatch { get; set; }
        public bool HasOverflow { get; set; }
        public long GrossRequiredQuantity { get; set; }
        public long AlreadyQuotedQuantity { get; set; }
        public List<string> RuleIds { get; } = [];
        public List<AutomaticWindowRuleSizeCalculation> Details { get; } = [];
        public HashSet<string> Aliases { get; } = new(StringComparer.Ordinal);
    }
}
