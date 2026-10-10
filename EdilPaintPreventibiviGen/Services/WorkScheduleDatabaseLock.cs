using EdilPaintPreventibiviGen.Data;
using Microsoft.EntityFrameworkCore;

namespace EdilPaintPreventibiviGen.Services;

internal static class WorkScheduleDatabaseLock
{
    internal const string Resource = "EdilPaint.SharedStaffPlanning";

    internal static Task AcquireAsync(AppDbContext db, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("Il blocco di programmazione richiede una transazione attiva.");
        return db.Database.ExecuteSqlRawAsync(db.Database.IsNpgsql() ? """
            SET LOCAL lock_timeout = '15s';
            SELECT pg_advisory_xact_lock(hashtext('EdilPaint.SharedStaffPlanning'));
            """ : """
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = 'EdilPaint.SharedStaffPlanning',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @result < 0
                THROW 50001, 'La programmazione viene aggiornata da un altro PC. Riprova tra qualche secondo.', 1;
            """, token);
    }
}
