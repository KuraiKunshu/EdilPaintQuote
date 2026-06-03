using EdilPaintPreventibiviGen.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EdilPaintPreventibiviGen.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260601150000_AddQuoteCostsPdfFiles")]
public sealed class AddQuoteCostsPdfFiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[QuoteCostsPdfFiles]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[QuoteCostsPdfFiles]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [QuoteId] INT NOT NULL,
                    [FileName] NVARCHAR(500) NOT NULL,
                    [ContentType] NVARCHAR(200) NOT NULL,
                    [Content] VARBINARY(MAX) NOT NULL,
                    [ImportedAt] DATETIME2 NOT NULL,
                    CONSTRAINT [FK_QuoteCostsPdfFiles_Quotes_QuoteId]
                        FOREIGN KEY ([QuoteId]) REFERENCES [dbo].[Quotes]([Id]) ON DELETE CASCADE,
                    CONSTRAINT [AK_QuoteCostsPdfFiles_QuoteId] UNIQUE ([QuoteId])
                );
                CREATE INDEX [IX_QuoteCostsPdfFiles_QuoteId] ON [dbo].[QuoteCostsPdfFiles]([QuoteId]);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[QuoteCostsPdfFiles]', N'U') IS NOT NULL
            BEGIN
                DROP TABLE [dbo].[QuoteCostsPdfFiles];
            END
            """);
    }
}
