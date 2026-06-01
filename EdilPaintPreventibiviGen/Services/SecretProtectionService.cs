using System;
using System.Security.Cryptography;
using System.Text;

namespace EdilPaintPreventibiviGen.Services;

public static class SecretProtectionService
{
    private const string Prefix = "dpapi:";
    private const string DatabasePurpose = "DatabaseSettings.v1";

    public static string Protect(string value, string purpose = DatabasePurpose)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        byte[] encrypted = ProtectedData.Protect(bytes, GetEntropy(purpose), DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(encrypted);
    }

    public static string Unprotect(string value, string purpose = DatabasePurpose)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            return value;

        try
        {
            byte[] encrypted = Convert.FromBase64String(value[Prefix.Length..]);
            byte[] decrypted = ProtectedData.Unprotect(encrypted, GetEntropy(purpose), DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new InvalidOperationException(
                "I dati protetti non possono essere decifrati da questo utente Windows. Inseriscili nuovamente nelle impostazioni.",
                ex);
        }
    }

    private static byte[] GetEntropy(string purpose) =>
        Encoding.UTF8.GetBytes($"EdilPaintPreventivi.{purpose}");
}
