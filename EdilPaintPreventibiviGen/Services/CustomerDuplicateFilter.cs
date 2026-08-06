using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

/// <summary>
/// Compatta solo la vista dei clienti con contenuto visibile equivalente.
/// Non modifica i record ricevuti e non richiede alcuna cancellazione nel database.
/// </summary>
public static class CustomerDuplicateFilter
{
    public static CustomerDuplicateFilterResult Compact(
        IEnumerable<Customer> customers,
        IEnumerable<Guid>? protectedIds = null)
    {
        ArgumentNullException.ThrowIfNull(customers);

        var source = customers.ToList();
        var protectedIdSet = protectedIds?
            .Where(syncId => syncId != Guid.Empty)
            .ToHashSet() ?? [];
        var keptIds = new HashSet<Guid>();
        var ignoredIds = new HashSet<Guid>();

        foreach (var group in source.GroupBy(
                     customer => customer,
                     VisibleCustomerContentComparer.Instance))
        {
            var groupCustomers = group.ToList();
            var stableCustomers = groupCustomers
                .Where(customer => customer.SyncId != Guid.Empty)
                .ToList();
            var protectedCustomers = stableCustomers
                .Where(customer =>
                    protectedIdSet.Contains(customer.SyncId))
                .ToList();

            if (protectedCustomers.Count == 0 && stableCustomers.Count > 0)
            {
                // Guid.Empty e' un alias legacy non indirizzabile: resta
                // visibile per sicurezza, ma non puo' sostituire il canonico
                // stabile scelto per il gruppo.
                protectedCustomers.Add(stableCustomers
                    .OrderBy(customer => customer.SyncId)
                    .First());
            }

            foreach (var customer in protectedCustomers)
            {
                if (customer.SyncId != Guid.Empty)
                    keptIds.Add(customer.SyncId);
            }

            foreach (var customer in stableCustomers.Except(protectedCustomers))
            {
                if (!keptIds.Contains(customer.SyncId))
                    ignoredIds.Add(customer.SyncId);
            }
        }

        // Mantiene l'ordinamento originale (nel normale flusso e' alfabetico),
        // mentre la scelta dell'ID da mostrare resta deterministica.
        var kept = source
            .Where(customer =>
                customer.SyncId == Guid.Empty ||
                keptIds.Contains(customer.SyncId))
            .ToList();

        ignoredIds.ExceptWith(keptIds);
        return new CustomerDuplicateFilterResult(kept, ignoredIds);
    }

    private sealed class VisibleCustomerContentComparer : IEqualityComparer<Customer>
    {
        public static VisibleCustomerContentComparer Instance { get; } = new();

        public bool Equals(Customer? left, Customer? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left is null || right is null)
                return false;

            return StringComparer.OrdinalIgnoreCase.Equals(Normalize(left.BusinessName), Normalize(right.BusinessName)) &&
                   StringComparer.OrdinalIgnoreCase.Equals(Normalize(left.Email), Normalize(right.Email)) &&
                   StringComparer.Ordinal.Equals(Exact(left.Address), Exact(right.Address)) &&
                   StringComparer.Ordinal.Equals(Exact(left.Phone), Exact(right.Phone)) &&
                   left.MaterialDiscount.Equals(right.MaterialDiscount) &&
                   left.LaborDiscount.Equals(right.LaborDiscount) &&
                   left.SupplierDiscount.Equals(right.SupplierDiscount) &&
                   left.IsSupplier == right.IsSupplier;
        }

        public int GetHashCode(Customer customer)
        {
            var hash = new HashCode();
            hash.Add(Normalize(customer.BusinessName), StringComparer.OrdinalIgnoreCase);
            hash.Add(Normalize(customer.Email), StringComparer.OrdinalIgnoreCase);
            hash.Add(Exact(customer.Address), StringComparer.Ordinal);
            hash.Add(Exact(customer.Phone), StringComparer.Ordinal);
            hash.Add(customer.MaterialDiscount);
            hash.Add(customer.LaborDiscount);
            hash.Add(customer.SupplierDiscount);
            hash.Add(customer.IsSupplier);
            return hash.ToHashCode();
        }

        private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
        private static string Exact(string? value) => value ?? string.Empty;
    }
}

public sealed record CustomerDuplicateFilterResult(
    IReadOnlyList<Customer> Kept,
    IReadOnlySet<Guid> IgnoredIds);
