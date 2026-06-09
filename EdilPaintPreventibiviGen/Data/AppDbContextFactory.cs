using Microsoft.EntityFrameworkCore;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Data;

public static class AppDbContextFactory
{
    static AppDbContextFactory()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    private static DbContextOptions<AppDbContext> BuildOptions()
    {
        var database = App.AppSettings.Database;
        string connectionString = database.BuildConnectionString();

        var options = new DbContextOptionsBuilder<AppDbContext>();

        if (database.UsesPostgreSql)
        {
            options.UseNpgsql(connectionString, postgres =>
            {
                postgres.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null);
            });
        }
        else
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);
            });
        }

        return options.Options;
    }

    public static AppDbContext Create() => new AppDbContext(BuildOptions());
}
