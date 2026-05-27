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
    public async Task<List<Customer>> GetCustomersAsync()
    {
        await using var db = AppDbContextFactory.Create();

        return await db.Customers
            .AsNoTracking()
            .OrderBy(x => x.BusinessName)
            .Select(x => x.ToModel())
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<Customer> AddCustomerAsync(Customer customer)
    {
        await using var db = AppDbContextFactory.Create();

        // Controlla se esiste giÃ  un cliente con lo stesso nome (evita duplicati)
        var existing = await db.Customers
            .FirstOrDefaultAsync(x => x.BusinessName == customer.BusinessName);

        if (existing != null)
        {
            // Aggiorna i dati esistenti
            existing.Address = customer.Address;
            existing.Email = customer.Email;
            existing.Phone = customer.Phone;
            existing.MaterialDiscount = customer.MaterialDiscount;
            existing.LaborDiscount = customer.LaborDiscount;
            await db.SaveChangesAsync();
            return existing.ToModel();
        }

        // Nuovo cliente
        var entity = customer.ToEntity();
        db.Customers.Add(entity);
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

