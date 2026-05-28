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
    public async Task<List<Item>> GetLaborCatalogAsync()
    {
        await using var db = AppDbContextFactory.Create();

        return await db.LaborCatalog
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => x.ToModel())
            .ToListAsync();
    }

    public async Task SaveLaborCatalogAsync(IEnumerable<Item> labors)
    {
        await using var db = AppDbContextFactory.Create();

        db.LaborCatalog.RemoveRange(db.LaborCatalog);
        await db.SaveChangesAsync();

        var entities = labors.Select(x => x.ToLaborCatalogEntity()).ToList();
        await db.LaborCatalog.AddRangeAsync(entities);
        await db.SaveChangesAsync();
    }

    public async Task<List<Item>> GetPersonalMaterialsAsync()
    {
        await using var db = AppDbContextFactory.Create();

        return await db.PersonalMaterials
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => x.ToModel())
            .ToListAsync();
    }

    public async Task SavePersonalMaterialsAsync(IEnumerable<Item> materials)
    {
        await using var db = AppDbContextFactory.Create();

        db.PersonalMaterials.RemoveRange(db.PersonalMaterials);
        await db.SaveChangesAsync();

        var entities = materials.Select(x => x.ToPersonalMaterialEntity()).ToList();
        await db.PersonalMaterials.AddRangeAsync(entities);
        await db.SaveChangesAsync();
    }
}

