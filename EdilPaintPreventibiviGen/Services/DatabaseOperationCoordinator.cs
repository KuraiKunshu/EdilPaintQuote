namespace EdilPaintPreventibiviGen.Services;

/// <summary>
/// Serializza le brevi sequenze DB che non possono essere interlacciate: un
/// singolo step di sync e il read-modify-write interattivo di una bozza.
/// </summary>
internal static class DatabaseOperationCoordinator
{
    internal static SemaphoreSlim Gate { get; } = new(1, 1);

    internal static async Task EnsureInteractiveDatabaseReadyAsync(
        IDataService dataService,
        string operation,
        CancellationToken cancellationToken = default)
    {
        if (dataService is FallbackDataService fallback)
        {
            await fallback.EnsureInteractiveDatabaseReadyAsync(operation, cancellationToken);
            return;
        }

        if (dataService is SqlDataService sql &&
            !await sql.CanConnectAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                $"Database non disponibile durante: {operation}.");
        }
    }
}
