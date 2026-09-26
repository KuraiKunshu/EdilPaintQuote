namespace EdilPaintPreventibiviGen.Models;

public static class CustomerVatPreference
{
    public const string None = "Nessuna preferenza";
    public static IReadOnlyList<string> Options { get; } = [None, "22%", "10%", "RC 10%+22%", "esclusa"];

    public static string Normalize(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "22%" => "22%",
        "10%" => "10%",
        "RC 10%+22%" => "RC 10%+22%",
        "ESCLUSA" => "esclusa",
        _ => string.Empty
    };

    public static string Display(string? value) => Normalize(value) is { Length: > 0 } vat ? vat : None;

    public static string Resolve(string? customerPreference, string? billingPreference,
        bool hasBillingCustomer, string defaultVatType)
    {
        string preference = Normalize(hasBillingCustomer ? billingPreference : customerPreference);
        return preference.Length > 0 ? preference : defaultVatType;
    }
}
