using EdilPaintPreventibiviGen.Data;
using Microsoft.EntityFrameworkCore;

namespace EdilPaintPreventibiviGen.Services;

public partial class SqlDataService
{
    private bool _workScheduleSchemaReady;

    internal async Task EnsureWorkScheduleSchemaAsync(AppDbContext db, CancellationToken token)
    {
        if (Volatile.Read(ref _workScheduleSchemaReady)) return;
        await EnsureEmployeeSchemaAsync(db, token).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(BuildWorkScheduleSchemaSql(db.Database.IsNpgsql()), token).ConfigureAwait(false);
        Volatile.Write(ref _workScheduleSchemaReady, true);
    }

    internal static string BuildWorkScheduleSchemaSql(bool postgres) => postgres ? """
        BEGIN;
        SELECT pg_advisory_xact_lock(hashtext('EdilPaint.WorkScheduleSchema'));
        CREATE TABLE IF NOT EXISTS "WorkScheduleSettings" (
            "Id" integer NOT NULL PRIMARY KEY CHECK ("Id" = 1),
            "Revision" bigint NOT NULL,
            "FullDayStartMinutes" integer NOT NULL,
            "FullDayEndMinutes" integer NOT NULL,
            "MorningStartMinutes" integer NOT NULL,
            "MorningEndMinutes" integer NOT NULL,
            "AfternoonStartMinutes" integer NOT NULL,
            "AfternoonEndMinutes" integer NOT NULL,
            "UseEmployeeAbbreviations" boolean NOT NULL
        );
        CREATE TABLE IF NOT EXISTS "WorkScheduleEntries" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "QuoteId" integer NULL REFERENCES "Quotes" ("Id") ON DELETE RESTRICT,
            "Date" date NOT NULL,
            "StartMinutes" integer NOT NULL,
            "EndMinutes" integer NOT NULL,
            "SlotKind" integer NOT NULL,
            "SettingsRevision" bigint NOT NULL,
            "Kind" integer NOT NULL,
            "AbsenceReason" varchar(250) NOT NULL,
            "Notes" text NOT NULL,
            "Status" integer NOT NULL,
            "Revision" bigint NOT NULL,
            "IsDeleted" boolean NOT NULL
        );
        CREATE TABLE IF NOT EXISTS "WorkScheduleAssignments" (
            "EntryId" uuid NOT NULL REFERENCES "WorkScheduleEntries" ("Id") ON DELETE CASCADE,
            "EmployeeId" uuid NOT NULL REFERENCES "Employees" ("Id") ON DELETE RESTRICT,
            PRIMARY KEY ("EntryId", "EmployeeId")
        );
        CREATE INDEX IF NOT EXISTS "IX_WorkScheduleEntries_Date" ON "WorkScheduleEntries" ("Date");
        CREATE INDEX IF NOT EXISTS "IX_WorkScheduleEntries_QuoteId" ON "WorkScheduleEntries" ("QuoteId");
        CREATE INDEX IF NOT EXISTS "IX_WorkScheduleAssignments_EmployeeId" ON "WorkScheduleAssignments" ("EmployeeId");
        INSERT INTO "WorkScheduleSettings" ("Id", "Revision", "FullDayStartMinutes", "FullDayEndMinutes",
            "MorningStartMinutes", "MorningEndMinutes", "AfternoonStartMinutes", "AfternoonEndMinutes", "UseEmployeeAbbreviations")
        VALUES (1, 1, 480, 1020, 480, 720, 780, 1020, TRUE) ON CONFLICT ("Id") DO NOTHING;
        COMMIT;
        """ : """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        DECLARE @lockResult int;
        EXEC @lockResult = sp_getapplock @Resource = 'EdilPaint.WorkScheduleSchema',
            @LockMode = 'Exclusive', @LockOwner = 'Transaction';
        IF @lockResult < 0
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50001, 'Impossibile preparare il calendario condiviso.', 1;
        END;
        IF OBJECT_ID(N'[dbo].[WorkScheduleSettings]', N'U') IS NULL
            CREATE TABLE [dbo].[WorkScheduleSettings] (
                [Id] int NOT NULL PRIMARY KEY CHECK ([Id] = 1),
                [Revision] bigint NOT NULL,
                [FullDayStartMinutes] int NOT NULL,
                [FullDayEndMinutes] int NOT NULL,
                [MorningStartMinutes] int NOT NULL,
                [MorningEndMinutes] int NOT NULL,
                [AfternoonStartMinutes] int NOT NULL,
                [AfternoonEndMinutes] int NOT NULL,
                [UseEmployeeAbbreviations] bit NOT NULL
            );
        IF OBJECT_ID(N'[dbo].[WorkScheduleEntries]', N'U') IS NULL
            CREATE TABLE [dbo].[WorkScheduleEntries] (
                [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                [QuoteId] int NULL REFERENCES [dbo].[Quotes] ([Id]),
                [Date] date NOT NULL,
                [StartMinutes] int NOT NULL,
                [EndMinutes] int NOT NULL,
                [SlotKind] int NOT NULL,
                [SettingsRevision] bigint NOT NULL,
                [Kind] int NOT NULL,
                [AbsenceReason] nvarchar(250) NOT NULL,
                [Notes] nvarchar(max) NOT NULL,
                [Status] int NOT NULL,
                [Revision] bigint NOT NULL,
                [IsDeleted] bit NOT NULL
            );
        IF OBJECT_ID(N'[dbo].[WorkScheduleAssignments]', N'U') IS NULL
            CREATE TABLE [dbo].[WorkScheduleAssignments] (
                [EntryId] uniqueidentifier NOT NULL REFERENCES [dbo].[WorkScheduleEntries] ([Id]) ON DELETE CASCADE,
                [EmployeeId] uniqueidentifier NOT NULL REFERENCES [dbo].[Employees] ([Id]),
                PRIMARY KEY ([EntryId], [EmployeeId])
            );
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkScheduleEntries_Date' AND object_id = OBJECT_ID(N'[dbo].[WorkScheduleEntries]'))
            CREATE INDEX [IX_WorkScheduleEntries_Date] ON [dbo].[WorkScheduleEntries] ([Date]);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkScheduleEntries_QuoteId' AND object_id = OBJECT_ID(N'[dbo].[WorkScheduleEntries]'))
            CREATE INDEX [IX_WorkScheduleEntries_QuoteId] ON [dbo].[WorkScheduleEntries] ([QuoteId]);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkScheduleAssignments_EmployeeId' AND object_id = OBJECT_ID(N'[dbo].[WorkScheduleAssignments]'))
            CREATE INDEX [IX_WorkScheduleAssignments_EmployeeId] ON [dbo].[WorkScheduleAssignments] ([EmployeeId]);
        IF NOT EXISTS (SELECT 1 FROM [dbo].[WorkScheduleSettings] WHERE [Id] = 1)
            INSERT INTO [dbo].[WorkScheduleSettings] ([Id], [Revision], [FullDayStartMinutes], [FullDayEndMinutes],
                [MorningStartMinutes], [MorningEndMinutes], [AfternoonStartMinutes], [AfternoonEndMinutes], [UseEmployeeAbbreviations])
            VALUES (1, 1, 480, 1020, 480, 720, 780, 1020, 1);
        COMMIT TRANSACTION;
        """;
}
