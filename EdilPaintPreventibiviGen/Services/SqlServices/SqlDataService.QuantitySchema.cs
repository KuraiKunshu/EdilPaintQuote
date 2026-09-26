using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Models;
using Microsoft.EntityFrameworkCore;

namespace EdilPaintPreventibiviGen.Services;

public partial class SqlDataService
{
    private static async Task EnsureQuantitySchemaAsync(AppDbContext db, CancellationToken token)
    {
        foreach (string table in new[] { "LaborCatalog", "PersonalMaterials", "QuoteMaterials", "QuoteLabors" })
        {
            bool quoteLine = table is "QuoteMaterials" or "QuoteLabors";
            await db.Database.ExecuteSqlRawAsync(BuildQuantitySchemaSql(table, db.Database.IsNpgsql(), quoteLine), token);
        }
    }

    internal static string BuildQuantitySchemaSql(string table, bool postgres, bool quoteLine)
    {
        if (table is not ("LaborCatalog" or "PersonalMaterials" or "QuoteMaterials" or "QuoteLabors"))
            throw new ArgumentException("Tabella non supportata.", nameof(table));
        if (postgres)
            return $"""
                ALTER TABLE "{table}" ADD COLUMN IF NOT EXISTS "UnitOfMeasure" varchar(16) NOT NULL DEFAULT 'pz';
                """ + (quoteLine ? $"""

                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema()
                        AND table_name = '{table}' AND column_name = 'Quantity'
                        AND (data_type = 'integer' OR (data_type = 'numeric' AND numeric_precision = 18 AND numeric_scale = 3))) THEN
                        ALTER TABLE "{table}" ALTER COLUMN "Quantity" TYPE numeric({QuantityValue.Precision},{QuantityValue.Scale})
                            USING "Quantity"::numeric({QuantityValue.Precision},{QuantityValue.Scale});
                    END IF;
                END $$;
                """ : "");
        return $"""
            IF COL_LENGTH(N'[dbo].[{table}]', N'UnitOfMeasure') IS NULL
                ALTER TABLE [dbo].[{table}] ADD [UnitOfMeasure] nvarchar(16) NOT NULL DEFAULT 'pz';
            """ + (quoteLine ? $"""

            IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
                WHERE c.object_id = OBJECT_ID(N'[dbo].[{table}]') AND c.name = 'Quantity'
                    AND (t.name = 'int' OR (t.name IN ('decimal', 'numeric') AND c.precision = 18 AND c.scale = 3)))
                ALTER TABLE [dbo].[{table}] ALTER COLUMN [Quantity] decimal({QuantityValue.Precision},{QuantityValue.Scale}) NOT NULL;
            """ : "");
    }
}
