namespace EdilPaintPreventibiviGen.Android.Services;

public sealed class CredentialStore
{
    private const string ConnectionStringKey = "neon_connection_string";

    public async Task<string> GetConnectionStringAsync()
    {
        return await SecureStorage.Default.GetAsync(ConnectionStringKey) ?? string.Empty;
    }

    public async Task SaveConnectionStringAsync(string connectionString)
    {
        await SecureStorage.Default.SetAsync(ConnectionStringKey, connectionString);
    }

    public void ClearConnectionString()
    {
        SecureStorage.Default.Remove(ConnectionStringKey);
    }
}
