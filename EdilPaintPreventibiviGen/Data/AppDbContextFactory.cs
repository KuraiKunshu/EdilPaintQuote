using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace EdilPaintPreventibiviGen.Data;

public static class AppDbContextFactory
{
    private static readonly DbContextOptions<AppDbContext> _options = BuildOptions();

    private static DbContextOptions<AppDbContext> BuildOptions()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? throw new InvalidOperationException("Connection string 'DefaultConnection' non trovata.");

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

    public static AppDbContext Create() => new AppDbContext(_options);
}