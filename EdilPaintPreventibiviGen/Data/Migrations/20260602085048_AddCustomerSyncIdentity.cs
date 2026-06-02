using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EdilPaintPreventibiviGen.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerSyncIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'[dbo].[Customers]', N'SyncId') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[Customers] ADD [SyncId] UNIQUEIDENTIFIER NULL;
                    UPDATE [dbo].[Customers] SET [SyncId] = NEWID() WHERE [SyncId] IS NULL;
                    ALTER TABLE [dbo].[Customers] ALTER COLUMN [SyncId] UNIQUEIDENTIFIER NOT NULL;
                END

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'[dbo].[Customers]')
                      AND name = 'IX_Customers_SyncId'
                )
                BEGIN
                    CREATE UNIQUE INDEX [IX_Customers_SyncId] ON [dbo].[Customers]([SyncId]);
                END

                IF COL_LENGTH(N'[dbo].[Customers]', N'IsDeleted') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[Customers] ADD [IsDeleted] BIT NOT NULL DEFAULT 0;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'[dbo].[Customers]')
                      AND name = 'IX_Customers_SyncId'
                )
                BEGIN
                    DROP INDEX [IX_Customers_SyncId] ON [dbo].[Customers];
                END

                IF COL_LENGTH(N'[dbo].[Customers]', N'IsDeleted') IS NOT NULL
                    ALTER TABLE [dbo].[Customers] DROP COLUMN [IsDeleted];

                IF COL_LENGTH(N'[dbo].[Customers]', N'SyncId') IS NOT NULL
                    ALTER TABLE [dbo].[Customers] DROP COLUMN [SyncId];
                """);
        }
    }
}
