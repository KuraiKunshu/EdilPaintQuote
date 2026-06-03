using EdilPaintPreventibiviGen.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EdilPaintPreventibiviGen.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202606010001_IntroduceVersionedSchema")]
public sealed class IntroduceVersionedSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'[dbo].[QuoteMaterials]', N'SortOrder') IS NULL
            BEGIN
                ALTER TABLE [dbo].[QuoteMaterials] ADD [SortOrder] INT NOT NULL DEFAULT 0;
            END

            IF COL_LENGTH(N'[dbo].[QuoteLabors]', N'SortOrder') IS NULL
            BEGIN
                ALTER TABLE [dbo].[QuoteLabors] ADD [SortOrder] INT NOT NULL DEFAULT 0;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'[dbo].[QuoteMaterials]', N'SortOrder') IS NOT NULL
            BEGIN
                ALTER TABLE [dbo].[QuoteMaterials] DROP COLUMN [SortOrder];
            END

            IF COL_LENGTH(N'[dbo].[QuoteLabors]', N'SortOrder') IS NOT NULL
            BEGIN
                ALTER TABLE [dbo].[QuoteLabors] DROP COLUMN [SortOrder];
            END
            """);
    }
}
