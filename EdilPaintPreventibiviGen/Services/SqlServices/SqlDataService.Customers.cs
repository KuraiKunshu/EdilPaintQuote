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
            .OrderBy(x => x.BusinessName)
            .Select(x => x.ToModel())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Customer> AddCustomerAsync(
        Customer customer,
        CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();

        // Controlla se esiste gia' un cliente con lo stesso nome.
        var existing = await db.Customers
            .FirstOrDefaultAsync(x => x.BusinessName == customer.BusinessName, cancellationToken);

        if (existing != null)
        {
            // Aggiorna i dati esistenti
            existing.Address = customer.Address;
            existing.Email = customer.Email;
            existing.Phone = customer.Phone;
            existing.MaterialDiscount = customer.MaterialDiscount;
            existing.LaborDiscount = customer.LaborDiscount;
            existing.LastModifiedUtc = customer.LastModifiedUtc;
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
        await using var db = AppDbContextFactory.Create();

        var entity = await db.Customers
            .FirstOrDefaultAsync(x => x.BusinessName == originalBusinessName)
            ?? await db.Customers.FirstOrDefaultAsync(x => x.BusinessName == customer.BusinessName);

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
            entity.LastModifiedUtc = customer.LastModifiedUtc;
        }

        await db.SaveChangesAsync();
        return entity.ToModel();
    }

    public async Task DeleteCustomerAsync(string businessName)
    {
        await using var db = AppDbContextFactory.Create();
        var entity = await db.Customers.FirstOrDefaultAsync(x => x.BusinessName == businessName);
        if (entity == null) return;
        db.Customers.Remove(entity);
        await db.SaveChangesAsync();
    }
}

