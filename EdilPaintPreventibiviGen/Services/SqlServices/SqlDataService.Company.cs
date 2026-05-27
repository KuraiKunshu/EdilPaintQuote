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
    public async Task<Company?> GetCompanyAsync()
    {
        await using var db = AppDbContextFactory.Create();

        var entity = await db.CompanySettings
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync();

        return entity?.ToModel();
    }

    public async Task SaveCompanyAsync(Company company, string selectedLogo)
    {
        await using var db = AppDbContextFactory.Create();

        var entity = await db.CompanySettings.OrderBy(x => x.Id).FirstOrDefaultAsync();

        if (entity == null)
        {
            entity = company.ToEntity(selectedLogo);
            db.CompanySettings.Add(entity);
        }
        else
        {
            entity.Nome = company.Nome;
            entity.Indirizzo = company.Indirizzo;
            entity.Piva = company.Piva;
            entity.Email = company.Email;
            entity.LogosJson = System.Text.Json.JsonSerializer.Serialize(company.Logo ?? new List<string>());
            entity.LogoIndex = company.Logo_index;
            // NON abbassare mai il counter â€” prendi il massimo tra quello attuale nel DB e quello locale
            entity.Counter = Math.Max(entity.Counter, company.Counter);
            entity.PaymentTerms = company.Termini_pagamento;
            entity.SelectedLogo = selectedLogo;
        }

        Debug.WriteLine($"[SAVE COMPANY] Logo ricevuti: {string.Join(" | ", company.Logo ?? new List<string>())}");
        Debug.WriteLine($"[SAVE COMPANY] selectedLogo: {selectedLogo}");
        Debug.WriteLine($"[SAVE COMPANY] Counter salvato: {entity.Counter} (locale: {company.Counter})");
        await db.SaveChangesAsync();
    }

    public async Task<int> GetNextQuoteNumberAsync()
    {
        await using var db = AppDbContextFactory.Create();

        // UPDATE atomico: incrementa e restituisce il nuovo valore in un'unica operazione SQL
        // Questo evita race condition tra piÃ¹ PC che chiamano contemporaneamente
        var result = await db.Database.SqlQueryRaw<int>("""
                                                        UPDATE TOP(1) [dbo].[CompanySettings]
                                                        SET [Counter] = [Counter] + 1
                                                        OUTPUT INSERTED.[Counter]
                                                        """).ToListAsync();

        if (result.Count > 0)
            return result[0];

        // Fallback: nessuna riga trovata, crea il record base
        var newSettings = new CompanySettingsEntity { Counter = 1 };
        db.CompanySettings.Add(newSettings);
        await db.SaveChangesAsync();
        return newSettings.Counter;
    }
}

