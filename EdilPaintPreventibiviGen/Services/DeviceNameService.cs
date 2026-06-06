namespace EdilPaintPreventibiviGen.Services;

internal static class DeviceNameService
{
    public static string GetCurrentDeviceName()
    {
        try
        {
            string? configured = EdilPaintPreventibiviGen.App.AppSettings?.App.GetEffectiveDeviceName();
            if (!string.IsNullOrWhiteSpace(configured))
                return configured.Trim();
        }
        catch
        {
        }

        try
        {
            return string.IsNullOrWhiteSpace(Environment.MachineName)
                ? "PC sconosciuto"
                : Environment.MachineName;
        }
        catch
        {
            return "PC sconosciuto";
        }
    }
}
