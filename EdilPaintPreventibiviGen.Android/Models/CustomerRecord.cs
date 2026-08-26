using System.Globalization;

namespace EdilPaintPreventibiviGen.Android.Models;

public sealed class CustomerRecord
{
    private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

    public int Id { get; set; }
    public Guid SyncId { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public double MaterialDiscount { get; set; }
    public double LaborDiscount { get; set; }
    public DateTime LastModifiedUtc { get; set; }

    public string ContactDisplay
    {
        get
        {
            var values = new[] { Phone, Email }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim());
            string result = string.Join(" - ", values);
            return string.IsNullOrWhiteSpace(result) ? "Nessun contatto" : result;
        }
    }

    public string AddressDisplay => string.IsNullOrWhiteSpace(Address) ? "Indirizzo non inserito" : Address.Trim();
    public string DiscountsDisplay => $"Materiali {MaterialDiscount.ToString("0.#", ItalianCulture)}% - Lavori {LaborDiscount.ToString("0.#", ItalianCulture)}%";

    public CustomerRecord Clone() => new()
    {
        Id = Id,
        SyncId = SyncId,
        BusinessName = BusinessName,
        Address = Address,
        Email = Email,
        Phone = Phone,
        MaterialDiscount = MaterialDiscount,
        LaborDiscount = LaborDiscount,
        LastModifiedUtc = LastModifiedUtc
    };

    public override string ToString() => BusinessName;
}

public sealed class CustomerOption
{
    public CustomerOption(string label, CustomerRecord? customer = null)
    {
        Label = label;
        Customer = customer;
    }

    public string Label { get; }
    public CustomerRecord? Customer { get; }
    public override string ToString() => Label;
}
