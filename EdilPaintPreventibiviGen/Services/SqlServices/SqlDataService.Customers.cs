using System.Diagnostics;
using System.Text.Json;
using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Data.Mappers;
using EdilPaintPreventibiviGen.Models;
using Microsoft.EntityFrameworkCore;

namespace EdilPaintPreventibiviGen.Services;
public partial class SqlDataService
{
    public async Task<List<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();

        return await db.Customers
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.BusinessName)
            .Select(x => x.ToModel())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<HashSet<Guid>> GetReferencedCustomerSyncIdsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();

        // Quotes applica il filtro globale !IsDeleted: proteggiamo quindi solo
        // gli ID realmente usati dai preventivi attivi, sui tre ruoli cliente.
        IQueryable<int> primaryCustomerIds = db.Quotes
            .AsNoTracking()
            .Where(quote => quote.CustomerId.HasValue)
            .Select(quote => quote.CustomerId!.Value);
        IQueryable<int> referenceCustomerIds = db.Quotes
            .AsNoTracking()
            .Where(quote => quote.ReferenceCustomerId.HasValue)
            .Select(quote => quote.ReferenceCustomerId!.Value);
        IQueryable<int> billingCustomerIds = db.Quotes
            .AsNoTracking()
            .Where(quote => quote.BillingCustomerId.HasValue)
            .Select(quote => quote.BillingCustomerId!.Value);

        var referencedEntityIds = primaryCustomerIds
            .Concat(referenceCustomerIds)
            .Concat(billingCustomerIds)
            .Distinct();

        var syncIds = await db.Customers
            .AsNoTracking()
            .Where(customer => referencedEntityIds.Contains(customer.Id))
            .Select(customer => customer.SyncId)
            .Where(syncId => syncId != Guid.Empty)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return syncIds.ToHashSet();
    }

    public async Task<(Customer? Customer, bool IsDeleted)> GetCustomerSyncStateAsync(
        Guid syncId,
        CancellationToken cancellationToken = default)
    {
        if (syncId == Guid.Empty)
            return (null, false);

        await using var db = AppDbContextFactory.Create();
        var entity = await db.Customers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(customer => customer.SyncId == syncId, cancellationToken)
            .ConfigureAwait(false);
        return entity == null
            ? (null, false)
            : (entity.ToModel(), entity.IsDeleted);
    }

    public Task<Customer> AddCustomerAsync(
        Customer customer,
        CancellationToken cancellationToken = default) =>
        AddCustomerWithExpectedVersionAsync(customer, cancellationToken, expectedLastModifiedUtc: null);

    public async Task<Customer> AddCustomerWithExpectedVersionAsync(
        Customer customer,
        CancellationToken cancellationToken,
        DateTime? expectedLastModifiedUtc)
    {
        NormalizeCustomerForSave(customer);

        await using var db = AppDbContextFactory.Create();
        if (customer.SyncId == Guid.Empty)
            customer.SyncId = Guid.NewGuid();

        var existing = await db.Customers
            .FirstOrDefaultAsync(x => x.SyncId == customer.SyncId, cancellationToken);

        if (expectedLastModifiedUtc.HasValue)
        {
            bool matchesExpectedState = expectedLastModifiedUtc.Value == default
                ? existing == null
                : existing != null &&
                  existing.LastModifiedUtc == expectedLastModifiedUtc.Value;
            if (!matchesExpectedState)
            {
                throw new DbUpdateConcurrencyException(
                    $"Il cliente {customer.BusinessName} e' stato modificato da un altro dispositivo.");
            }
        }

        if (existing != null)
        {
            // Aggiorna i dati esistenti
            existing.BusinessName = customer.BusinessName;
            existing.Address = customer.Address;
            existing.Email = customer.Email;
            existing.Phone = customer.Phone;
            existing.MaterialDiscount = customer.MaterialDiscount;
            existing.LaborDiscount = customer.LaborDiscount;
            existing.SupplierDiscount = customer.SupplierDiscount;
            existing.IsSupplier = customer.IsSupplier;
            existing.LastModifiedUtc = customer.LastModifiedUtc;
            existing.SyncId = customer.SyncId;
            existing.IsDeleted = false;
            await db.SaveChangesAsync(cancellationToken);
            return existing.ToModel();
        }

        // Nuovo cliente
        var entity = customer.ToEntity();
        db.Customers.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToModel();
    }

    public async Task<Customer> UpdateCustomerAsync(string originalBusinessName, Customer customer)
    {
        NormalizeCustomerForSave(customer);

        await using var db = AppDbContextFactory.Create();
        if (customer.SyncId == Guid.Empty)
            customer.SyncId = Guid.NewGuid();

        var entity = await db.Customers
            .FirstOrDefaultAsync(x => x.SyncId == customer.SyncId);

        if (entity == null)
        {
            entity = customer.ToEntity();
            db.Customers.Add(entity);
        }
        else
        {
            entity.BusinessName = customer.BusinessName;
            entity.Address = customer.Address;
            entity.Email = customer.Email;
            entity.Phone = customer.Phone;
            entity.MaterialDiscount = customer.MaterialDiscount;
            entity.LaborDiscount = customer.LaborDiscount;
            entity.SupplierDiscount = customer.SupplierDiscount;
            entity.IsSupplier = customer.IsSupplier;
            entity.LastModifiedUtc = customer.LastModifiedUtc;
            entity.SyncId = customer.SyncId;
            entity.IsDeleted = false;
        }

        await db.SaveChangesAsync();
        return entity.ToModel();
    }

    private static void NormalizeCustomerForSave(Customer customer)
    {
        customer.BusinessName = (customer.BusinessName ?? string.Empty).Trim();
        customer.Address = customer.Address?.Trim() ?? string.Empty;
        customer.Email = customer.Email?.Trim() ?? string.Empty;
        customer.Phone = customer.Phone?.Trim() ?? string.Empty;
        customer.SupplierDiscount = Math.Clamp(customer.SupplierDiscount, 0, 100);

        if (string.IsNullOrWhiteSpace(customer.BusinessName))
            throw new InvalidOperationException("Impossibile salvare un cliente senza ragione sociale.");

        if (customer.LastModifiedUtc == default)
            customer.LastModifiedUtc = DateTime.UtcNow;
    }

    public Task DeleteCustomerAsync(Customer customer) =>
        DeleteCustomerAsync(customer.SyncId, customer.BusinessName);

    public async Task DeleteCustomerAsync(Guid syncId, string businessName)
    {
        await using var db = AppDbContextFactory.Create();
        var entity = syncId != Guid.Empty
            ? await db.Customers.FirstOrDefaultAsync(x => x.SyncId == syncId)
            : await db.Customers.FirstOrDefaultAsync(x => x.BusinessName == businessName);
        if (entity == null) return;
        entity.IsDeleted = true;
        entity.LastModifiedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<List<Customer>> GetDeletedCustomersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();
        return await db.Customers
            .AsNoTracking()
            .Where(x => x.IsDeleted)
            .Select(x => x.ToModel())
            .ToListAsync(cancellationToken);
    }
}

