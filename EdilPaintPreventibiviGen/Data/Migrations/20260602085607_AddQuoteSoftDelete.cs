using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EdilPaintPreventibiviGen.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'[dbo].[Quotes]', N'IsDeleted') IS NULL
                    ALTER TABLE [dbo].[Quotes] ADD [IsDeleted] BIT NOT NULL DEFAULT 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'[dbo].[Quotes]', N'IsDeleted') IS NOT NULL
                    ALTER TABLE [dbo].[Quotes] DROP COLUMN [IsDeleted];
                """);
        }
    }
}
