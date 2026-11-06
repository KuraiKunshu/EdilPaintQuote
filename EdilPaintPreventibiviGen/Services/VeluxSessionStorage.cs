using System;
using System.IO;
using System.Net;
using System.Text.Json;

namespace EdilPaintPreventibiviGen.Services;

public static class VeluxSessionStorage
{
    private const string ProtectionPurpose = "VeluxSession.v1";
    private static long _sessionVersion;
    internal static long SessionVersion => Interlocked.Read(ref _sessionVersion);

    public static string GetStoragePath()
    {
        string folder = CompanyInstallationService.RootDirectory;

        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "velux_storage.json");
    }

    public static string? Load()
    {
        string path = GetStoragePath();
        if (!File.Exists(path))
            return null;

        string storedValue = File.ReadAllText(path);
        string json = SecretProtectionService.Unprotect(storedValue, ProtectionPurpose);

        if (!storedValue.StartsWith("dpapi:", StringComparison.Ordinal))
            Save(json);

        return json;
    }

    public static void Save(string json)
    {
        File.WriteAllText(GetStoragePath(), SecretProtectionService.Protect(json, ProtectionPurpose));
        Interlocked.Increment(ref _sessionVersion);
    }

    public static void Clear()
    {
        string path = GetStoragePath();
        if (File.Exists(path))
            File.Delete(path);
        Interlocked.Increment(ref _sessionVersion);
    }

    internal static void RestoreCookies(CookieContainer container, string json, Uri? origin = null)
    {
        origin ??= VeluxEndpoints.BaseUri;
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException) { return; }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("cookies", out var cookies) ||
                cookies.ValueKind != JsonValueKind.Array) return;

            foreach (var stored in cookies.EnumerateArray())
            {
                try
                {
                    if (stored.ValueKind != JsonValueKind.Object ||
                        !stored.TryGetProperty("name", out var name) ||
                        !stored.TryGetProperty("value", out var value) ||
                        !stored.TryGetProperty("domain", out var domainValue)) continue;

                    string domain = domainValue.GetString()?.Trim() ?? string.Empty;
                    string cookieHost = domain.TrimStart('.');
                    string activeHost = origin.Host;
                    if (!string.Equals(activeHost, cookieHost, StringComparison.OrdinalIgnoreCase) &&
                        !(cookieHost.Contains('.') && activeHost.EndsWith("." + cookieHost, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    string path = stored.TryGetProperty("path", out var pathValue) ? pathValue.GetString() ?? "/" : "/";
                    if (path.Length == 0) path = "/";
                    if (!path.StartsWith('/')) continue;
                    var cookie = new Cookie(name.GetString()!, value.GetString()!, path, domain);
                    if (stored.TryGetProperty("secure", out var secure)) cookie.Secure = secure.GetBoolean();
                    if (stored.TryGetProperty("httpOnly", out var httpOnly)) cookie.HttpOnly = httpOnly.GetBoolean();
                    container.Add(origin, cookie);
                }
                catch (Exception ex) when (ex is CookieException or ArgumentException or InvalidOperationException)
                {
                    // Ignore this cookie while retaining any valid entries in the saved session.
                }
            }
        }
    }
}
