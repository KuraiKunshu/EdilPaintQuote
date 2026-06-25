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
        var existing = await db.LaborCatalog.ToListAsync();
        foreach (var labor in labors.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
        {
            var entity = (labor.PersistentId > 0
                    ? existing.FirstOrDefault(x => x.Id == labor.PersistentId)
                    : null)
                ?? existing.FirstOrDefault(x => x.Name.Equals(labor.Name.Trim(), StringComparison.OrdinalIgnoreCase));

            if (entity == null)
            {
                entity = new LaborCatalogEntity();
                db.LaborCatalog.Add(entity);
                existing.Add(entity);
            }

            entity.Name = labor.Name.Trim();
            entity.Description = labor.Description?.Trim() ?? string.Empty;
            entity.UnitPrice = labor.UnitPrice;
            labor.PersistentId = entity.Id;
        }

        await db.SaveChangesAsync();
        foreach (var labor in labors)
        {
            if (labor.PersistentId == 0)
                labor.PersistentId = existing.FirstOrDefault(x =>
                    x.Name.Equals(labor.Name.Trim(), StringComparison.OrdinalIgnoreCase))?.Id ?? 0;
        }
    }

    public async Task DeleteLaborCatalogItemAsync(Item labor, CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();
        var entity = await db.LaborCatalog.FirstOrDefaultAsync(x =>
            (labor.PersistentId > 0 && x.Id == labor.PersistentId) || x.Name == labor.Name,
            cancellationToken);
        if (entity == null) return;
        db.LaborCatalog.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
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
        var existing = await db.PersonalMaterials.ToListAsync();
        foreach (var material in materials.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
        {
            var entity = (material.PersistentId > 0
                    ? existing.FirstOrDefault(x => x.Id == material.PersistentId)
                    : null)
                ?? existing.FirstOrDefault(x => x.Name.Equals(material.Name.Trim(), StringComparison.OrdinalIgnoreCase));

            if (entity == null)
            {
                entity = new PersonalMaterialEntity();
                db.PersonalMaterials.Add(entity);
                existing.Add(entity);
            }

            entity.Name = material.Name.Trim();
            entity.Description = material.Description?.Trim() ?? string.Empty;
            entity.UnitPrice = material.UnitPrice;
            entity.IsSignificant = material.IsSignificant;
            material.PersistentId = entity.Id;
        }

        await db.SaveChangesAsync();
        foreach (var material in materials)
        {
            if (material.PersistentId == 0)
                material.PersistentId = existing.FirstOrDefault(x =>
                    x.Name.Equals(material.Name.Trim(), StringComparison.OrdinalIgnoreCase))?.Id ?? 0;
        }
    }

    public async Task DeletePersonalMaterialAsync(Item material, CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();
        var entity = await db.PersonalMaterials.FirstOrDefaultAsync(x =>
            (material.PersistentId > 0 && x.Id == material.PersistentId) || x.Name == material.Name,
            cancellationToken);
        if (entity == null) return;
        db.PersonalMaterials.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

}

