using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Data.Entities;

public class QuoteEntity
{
	public int Id { get; set; }
	public string QuoteNumber { get; set; } = string.Empty;
	public DateTime Date { get; set; }

	public int? CustomerId { get; set; }
	public CustomerEntity? Customer { get; set; }

	public int? ReferenceCustomerId { get; set; }
	public CustomerEntity? ReferenceCustomer { get; set; }

	public string SiteName { get; set; } = string.Empty;
	public string BillingCustomerName { get; set; } = string.Empty;

	public string PdfPath { get; set; } = string.Empty;
	public string PaymentTerms { get; set; } = string.Empty;
	public string IvaType { get; set; } = "esclusa";
	public string Notes { get; set; } = string.Empty;

	public double Imponibile { get; set; }
	public double MaterialDiscount { get; set; }
	public double LaborDiscount { get; set; }
	public double Total { get; set; }
	public QuoteStatus Status { get; set; } = QuoteStatus.Finalizzato;
	public string CreatedByDevice { get; set; } = string.Empty;
	public string LastModifiedByDevice { get; set; } = string.Empty;
	public DateTime? SentAtUtc { get; set; }
	public string SentMethod { get; set; } = string.Empty;
	public string SentRecipient { get; set; } = string.Empty;
	public string SentByDevice { get; set; } = string.Empty;
	public DateTime? LastReminderAtUtc { get; set; }
	public int ReminderCount { get; set; }
	public string LastReminderByDevice { get; set; } = string.Empty;
	public string EventsJson { get; set; } = string.Empty;
	
	// Collaborazione con altra ditta
	public bool IsJointVenture { get; set; }
	public string PartnerCompanyName { get; set; } = string.Empty;
	/// <summary>
	/// JSON serializzato di CostAllocations (OurCosts, PartnerCosts, AdditionalCosts).
	/// Solo uso interno — NON finisce nel PDF.
	/// </summary>
	public string CostAllocationsJson { get; set; } = string.Empty;
	
	public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;
	public long Revision { get; set; }
	public string SyncHash { get; set; } = string.Empty;
	public bool IsDeleted { get; set; }
	
	public ICollection<QuoteMaterialEntity> Materials { get; set; } = new List<QuoteMaterialEntity>();
	public ICollection<QuoteLaborEntity> Labors { get; set; } = new List<QuoteLaborEntity>();
	public ICollection<QuoteAttachmentEntity> Attachments { get; set; } = new List<QuoteAttachmentEntity>();
}
