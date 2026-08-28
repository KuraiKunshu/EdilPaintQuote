using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EdilPaintPreventibiviGen.Models;

public enum QuoteStatus
{
	Finalizzato,
	Spedito,
	Confermato,
	Finito,
	Rifiutato,
	Bozza,
	DaInviare,
	DaSollecitare,
	Archiviato
}

public sealed class QuoteEventEntry
{
	public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
	public string DeviceName { get; set; } = string.Empty;
	public string EventType { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
}

public sealed class QuoteSendInfo
{
	public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
	public string Method { get; set; } = string.Empty;
	public string Recipient { get; set; } = string.Empty;
	public string DeviceName { get; set; } = string.Empty;
}

public sealed class QuoteReminderInfo
{
	public DateTime ReminderAtUtc { get; set; } = DateTime.UtcNow;
	public string DeviceName { get; set; } = string.Empty;
}

public sealed class QuoteSupplierInfo
{
	public string SupplierName { get; set; } = string.Empty;
	public bool MaterialsOrderedByCustomer { get; set; }
	public DateTime? MaterialOrderDate { get; set; }
	public DateTime? ExpectedDeliveryDate { get; set; }
	public string MaterialStatus { get; set; } = string.Empty;
	public string DeviceName { get; set; } = string.Empty;
}

public class QuoteHistoryEntry
{
	public string QuoteNumber { get; set; } = string.Empty;
	public DateTime Date { get; set; }
	public string CustomerName { get; set; } = string.Empty;
	[JsonPropertyName("customerSyncId")]
	public Guid CustomerSyncId { get; set; }
	public string ReferenceName { get; set; } = string.Empty;
	[JsonPropertyName("referenceCustomerSyncId")]
	public Guid ReferenceCustomerSyncId { get; set; }
	public string SiteName { get; set; } = string.Empty;
	public string BillingCustomerName { get; set; } = string.Empty;
	[JsonPropertyName("billingCustomerSyncId")]
	public Guid BillingCustomerSyncId { get; set; }
	public string PdfPath { get; set; } = string.Empty;
	public string PaymentTerms { get; set; } = string.Empty;
	public string CustomerNotes { get; set; } = string.Empty;
	public string IvaType { get; set; } = "esclusa";
	public string Notes { get; set; } = string.Empty;
	public List<Item> Materials { get; set; } = new();
	public List<Item> Labors { get; set; } = new();
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
	public List<QuoteEventEntry> Events { get; set; } = new();
	public string SupplierName { get; set; } = string.Empty;
	[JsonPropertyName("materialsOrderedByCustomer")]
	public bool MaterialsOrderedByCustomer { get; set; }
	public DateTime? MaterialOrderDate { get; set; }
	public DateTime? ExpectedDeliveryDate { get; set; }
	public string MaterialStatus { get; set; } = string.Empty;
	[JsonPropertyName("realProfit")]
	public RealProfitSnapshot? RealProfit { get; set; }

	//-----------------collaborazione-------------
	// Collaborazione con altra ditta
	public bool IsJointVenture { get; set; }
	public string PartnerCompanyName { get; set; } = string.Empty;
	public List<CostAllocationItem> OurCosts { get; set; } = new();
	public List<CostAllocationItem> PartnerCosts { get; set; } = new();
	public List<CostAllocationItem> AdditionalCosts { get; set; } = new();

	//------------------
	public StoredFile? PdfFile { get; set; }
	public List<StoredFile> Attachments { get; set; } = new();
	[JsonPropertyName("hasCompleteAttachmentSnapshot")]
	public bool HasCompleteAttachmentSnapshot { get; set; }

	// Metadati di sincronizzazione
	[JsonPropertyName("lastModifiedUtc")]
	public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;
	
	[JsonPropertyName("syncHash")]
	public string SyncHash { get; set; } = string.Empty;

	[JsonPropertyName("baseVersionUtc")]
	public DateTime BaseVersionUtc { get; set; }

	[JsonPropertyName("revision")]
	public long Revision { get; set; }

	[JsonPropertyName("baseRevision")]
	public long BaseRevision { get; set; }

	[JsonPropertyName("hasPendingDatabaseWrite")]
	public bool HasPendingDatabaseWrite { get; set; }

	// Solo per la bozza locale: permette di riprendere in sicurezza la modifica
	// di un preventivo esistente mantenendo il controllo di concorrenza.
	[JsonPropertyName("isEditingExistingQuoteDraft")]
	public bool IsEditingExistingQuoteDraft { get; set; }

	[JsonPropertyName("isDraftQuoteNumberAllocated")]
	public bool IsDraftQuoteNumberAllocated { get; set; }

	[JsonPropertyName("wasCreatedByDraftAutosave")]
	public bool WasCreatedByDraftAutosave { get; set; }

	[JsonPropertyName("sharedDraftContentHash")]
	public string SharedDraftContentHash { get; set; } = string.Empty;
}
