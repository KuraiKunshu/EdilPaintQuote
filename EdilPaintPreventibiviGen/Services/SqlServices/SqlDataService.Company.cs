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
            // Non abbassare mai il counter: prendi il massimo tra DB e valore locale.
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

        // UPDATE atomico: allinea il counter al massimo numero preventivo gia' presente,
        // poi incrementa e restituisce il nuovo valore. Questo evita duplicati se il
        // counter rimane indietro rispetto allo storico condiviso.
        // La sintassi cambia tra SQL Server e PostgreSQL, quindi teniamo separati i due dialetti.
        var result = db.Database.IsNpgsql()
            ? await db.Database.SqlQueryRaw<int>("""
                                                WITH first_settings AS (
                                                    SELECT "Id"
                                                    FROM "CompanySettings"
                                                    ORDER BY "Id"
                                                    LIMIT 1
                                                ),
                                                max_quote AS (
                                                    SELECT COALESCE(MAX(
                                                        CASE
                                                            WHEN "QuoteNumber" ~ '^[0-9]+'
                                                            THEN substring("QuoteNumber" from '^[0-9]+')::integer
                                                            ELSE NULL
                                                        END
                                                    ), 0) AS "MaxQuoteNumber"
                                                    FROM "Quotes"
                                                    WHERE NOT "IsDeleted"
                                                )
                                                UPDATE "CompanySettings" AS settings
                                                SET "Counter" = GREATEST(settings."Counter", max_quote."MaxQuoteNumber") + 1
                                                FROM first_settings, max_quote
                                                WHERE settings."Id" = first_settings."Id"
                                                RETURNING settings."Counter"
                                                """).ToListAsync()
            : await db.Database.SqlQueryRaw<int>("""
                                                ;WITH FirstSettings AS (
                                                    SELECT TOP(1) [Id]
                                                    FROM [dbo].[CompanySettings]
                                                    ORDER BY [Id]
                                                ),
                                                MaxQuote AS (
                                                    SELECT COALESCE(MAX(TRY_CONVERT(INT,
                                                        LEFT([QuoteNumber], PATINDEX('%[^0-9]%', [QuoteNumber] + 'X') - 1)
                                                    )), 0) AS [MaxQuoteNumber]
                                                    FROM [dbo].[Quotes]
                                                    WHERE [IsDeleted] = 0
                                                )
                                                UPDATE settings
                                                SET [Counter] =
                                                    CASE
                                                        WHEN settings.[Counter] > MaxQuote.[MaxQuoteNumber]
                                                            THEN settings.[Counter] + 1
                                                        ELSE MaxQuote.[MaxQuoteNumber] + 1
                                                    END
                                                OUTPUT INSERTED.[Counter]
                                                FROM [dbo].[CompanySettings] AS settings
                                                INNER JOIN FirstSettings ON FirstSettings.[Id] = settings.[Id]
                                                CROSS JOIN MaxQuote
                                                """).ToListAsync();

        if (result.Count > 0)
            return result[0];

        // Fallback: nessuna riga trovata, crea il record base
        int maxExistingQuoteNumber = await GetMaxExistingQuoteNumberAsync(db);
        var newSettings = new CompanySettingsEntity { Counter = Math.Max(1, maxExistingQuoteNumber + 1) };
        db.CompanySettings.Add(newSettings);
        await db.SaveChangesAsync();
        return newSettings.Counter;
    }

    private static async Task<int> GetMaxExistingQuoteNumberAsync(AppDbContext db)
    {
        var result = db.Database.IsNpgsql()
            ? await db.Database.SqlQueryRaw<int>("""
                                                SELECT COALESCE(MAX(
                                                    CASE
                                                        WHEN "QuoteNumber" ~ '^[0-9]+'
                                                        THEN substring("QuoteNumber" from '^[0-9]+')::integer
                                                        ELSE NULL
                                                    END
                                                ), 0)
                                                FROM "Quotes"
                                                WHERE NOT "IsDeleted"
                                                """).ToListAsync()
            : await db.Database.SqlQueryRaw<int>("""
                                                SELECT COALESCE(MAX(TRY_CONVERT(INT,
                                                    LEFT([QuoteNumber], PATINDEX('%[^0-9]%', [QuoteNumber] + 'X') - 1)
                                                )), 0)
                                                FROM [dbo].[Quotes]
                                                WHERE [IsDeleted] = 0
                                                """).ToListAsync();

        return result.FirstOrDefault();
    }
}

