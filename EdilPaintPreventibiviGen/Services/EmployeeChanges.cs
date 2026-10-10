using System.Security.Cryptography;
using System.Text;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

internal sealed record EmployeeChange(EmployeeSettingsModel? Original, EmployeeSettingsModel? Updated)
{
    public Guid Id => (Original ?? Updated!).Id;
}

internal static class EmployeeChanges
{
    internal static List<EmployeeChange> Between(IReadOnlyList<EmployeeSettingsModel> original,
        IReadOnlyList<EmployeeSettingsModel> edited)
    {
        var before = original.ToDictionary(x => x.Id);
        var after = edited.Select(x => x.CreateValidatedCopy()).ToDictionary(x => x.Id);
        ValidateUniqueAbbreviations(after.Values);
        if (before.ContainsKey(Guid.Empty) || after.ContainsKey(Guid.Empty))
            throw new InvalidOperationException("Identificativo dipendente non valido.");

        return before.Keys.Union(after.Keys).Select(id =>
        {
            before.TryGetValue(id, out var previous);
            after.TryGetValue(id, out var current);
            return new EmployeeChange(previous, current);
        }).Where(change => change.Original == null || change.Updated == null ||
            change.Original.FirstName != change.Updated.FirstName ||
            change.Original.LastName != change.Updated.LastName ||
            change.Original.Abbreviation != change.Updated.Abbreviation).ToList();
    }

    // Validate the entire batch before mutating tracked entities. Untouched rows
    // are never written, so another PC's unrelated edits and additions survive.
    internal static List<EmployeeEntity> Apply(IReadOnlyList<EmployeeChange> changes,
        IReadOnlyDictionary<Guid, EmployeeEntity> stored)
    {
        foreach (var change in changes)
        {
            stored.TryGetValue(change.Id, out var entity);
            bool matches = change.Original == null ? entity == null :
                entity != null && !entity.IsDeleted && entity.Revision == change.Original.Revision;
            if (!matches)
                throw Conflict();
        }

        var changedIds = changes.Select(x => x.Id).ToHashSet();
        ValidateUniqueAbbreviations(stored.Values.Where(x => !x.IsDeleted && !changedIds.Contains(x.Id))
            .Select(ToModel).Concat(changes.Where(x => x.Updated != null).Select(x => x.Updated!)));

        var added = new List<EmployeeEntity>();
        foreach (var change in changes)
        {
            if (!stored.TryGetValue(change.Id, out var entity))
            {
                entity = new EmployeeEntity { Id = change.Id };
                added.Add(entity);
            }
            else
                entity.Revision = checked(entity.Revision + 1);

            entity.IsDeleted = change.Updated == null;
            if (change.Updated != null)
            {
                entity.FirstName = change.Updated.FirstName;
                entity.LastName = change.Updated.LastName;
                entity.Abbreviation = change.Updated.Abbreviation;
            }
        }
        return added;
    }

    internal static InvalidOperationException Conflict(Exception? inner = null) => new(
        "Uno dei dipendenti modificati è stato aggiornato o rimosso da un altro PC. " +
        "Ricarica l'elenco e applica nuovamente le tue modifiche.", inner);

    internal static EmployeeSettingsModel ToModel(EmployeeEntity entity) => new()
    {
        Id = entity.Id, FirstName = entity.FirstName, LastName = entity.LastName,
        Abbreviation = entity.Abbreviation, Revision = entity.Revision
    };

    internal static List<EmployeeEntity> LegacyImports(IEnumerable<EmployeeSettingsModel> legacy,
        IReadOnlyCollection<EmployeeEntity> stored)
    {
        var imports = new List<EmployeeEntity>();
        foreach (var employee in legacy.Where(x => !string.IsNullOrWhiteSpace(x.FirstName))
                     .Select(x => x.CreateValidatedCopy()).DistinctBy(NameKey))
        {
            string key = NameKey(employee);
            // Stable IDs make simultaneous imports of the same old local list idempotent.
            var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("EdilPaint.Employees.Legacy:" + key)).AsSpan(0, 16));
            if (stored.Any(x => x.Id == id))
                continue;

            bool alreadyPresent = stored.Any(x => x.Id == employee.Id ||
                NameKey(ToModel(x)) == key);
            imports.Add(new EmployeeEntity
            {
                Id = id, FirstName = employee.FirstName, LastName = employee.LastName,
                Abbreviation = employee.Abbreviation.Length > 0 && stored.Concat(imports).Any(x =>
                    !x.IsDeleted && x.Abbreviation == employee.Abbreviation) ? string.Empty : employee.Abbreviation,
                // A tombstone also records imports matched to another ID, preventing
                // old local settings from bringing a renamed/deleted employee back.
                IsDeleted = alreadyPresent
            });
        }
        return imports;
    }

    private static string NameKey(EmployeeSettingsModel employee) =>
        Normalize(employee.FirstName) + "\n" + Normalize(employee.LastName);

    internal static void ValidateUniqueAbbreviations(IEnumerable<EmployeeSettingsModel> employees)
    {
        var duplicate = employees.Where(x => !string.IsNullOrWhiteSpace(x.Abbreviation))
            .GroupBy(x => x.Abbreviation.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate != null)
            throw new InvalidOperationException($"La sigla '{duplicate.Key}' è già usata da un altro dipendente. Scegli sigle diverse oppure lascia il campo vuoto.");
    }

    private static string Normalize(string name) => string.Join(' ',
        name.Normalize(NormalizationForm.FormKC).Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
