using System.Collections.ObjectModel;

namespace EdilPaintPreventibiviGen.Android.Models;

public sealed class QuoteDraft
{
    public int Id { get; set; }
    public string QuoteNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public int? CustomerId { get; set; }
    public Guid CustomerSyncId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int? ReferenceCustomerId { get; set; }
    public Guid ReferenceCustomerSyncId { get; set; }
    public string ReferenceName { get; set; } = string.Empty;
    public int? BillingCustomerId { get; set; }
    public Guid BillingCustomerSyncId { get; set; }
    public string BillingCustomerName { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string PaymentTerms { get; set; } = string.Empty;
    public string CustomerNotes { get; set; } = string.Empty;
    public string IvaType { get; set; } = "esclusa";
    public string Notes { get; set; } = string.Empty;
    public double MaterialDiscount { get; set; }
    public double LaborDiscount { get; set; }
    public QuoteStatus Status { get; set; } = QuoteStatus.Bozza;
    public long Revision { get; set; }
    public ObservableCollection<QuoteLine> Materials { get; } = [];
    public ObservableCollection<QuoteLine> Labors { get; } = [];

    public bool IsNew => Id == 0;
    public string QuoteNumberDisplay => IsNew ? "Assegnato al salvataggio" : QuoteNumber;

    public static QuoteDraft FromDetail(QuoteDetail detail)
    {
        var draft = new QuoteDraft
        {
            Id = detail.Id,
            QuoteNumber = detail.QuoteNumber,
            Date = detail.Date,
            CustomerId = detail.CustomerId,
            CustomerSyncId = detail.CustomerSyncId,
            CustomerName = detail.CustomerName,
            ReferenceCustomerId = detail.ReferenceCustomerId,
            ReferenceCustomerSyncId = detail.ReferenceCustomerSyncId,
            ReferenceName = detail.ReferenceName,
            BillingCustomerId = detail.BillingCustomerId,
            BillingCustomerSyncId = detail.BillingCustomerSyncId,
            BillingCustomerName = detail.BillingCustomerName,
            SiteName = detail.SiteName,
            PaymentTerms = detail.PaymentTerms,
            CustomerNotes = detail.CustomerNotes,
            IvaType = detail.IvaType,
            Notes = detail.Notes,
            MaterialDiscount = detail.MaterialDiscount,
            LaborDiscount = detail.LaborDiscount,
            Status = detail.Status,
            Revision = detail.Revision
        };

        foreach (var line in detail.Materials)
            draft.Materials.Add(line.Clone());
        foreach (var line in detail.Labors)
            draft.Labors.Add(line.Clone());

        return draft;
    }
}

public sealed record QuoteSaveResult(int Id, string QuoteNumber, long Revision);

public sealed record QuoteEditorDefaults(string PaymentTerms);
