using Microsoft.EntityFrameworkCore;

namespace EdilPaintPreventibiviGen.Data;

public static class AppDbContextFactory
{
    private static DbContextOptions<AppDbContext> BuildOptions()
    {
        string connectionString = App.AppSettings.Database.BuildConnectionString();

        return new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);
            })
            .Options;
    }

    public static AppDbContext Create() => new AppDbContext(BuildOptions());
}
