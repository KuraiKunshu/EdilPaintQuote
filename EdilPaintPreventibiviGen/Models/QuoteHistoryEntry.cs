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

public class QuoteHistoryEntry
{
	public string QuoteNumber { get; set; } = string.Empty;
	public DateTime Date { get; set; }
	public string CustomerName { get; set; } = string.Empty;
	public string ReferenceName { get; set; } = string.Empty;
	public string PdfPath { get; set; } = string.Empty;
	public string PaymentTerms { get; set; } = string.Empty;
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
	[JsonIgnore]
	public bool HasCompleteAttachmentSnapshot { get; set; }

	// Metadati di sincronizzazione
	[JsonPropertyName("lastModifiedUtc")]
	public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;
	
	[JsonPropertyName("syncHash")]
	public string SyncHash { get; set; } = string.Empty;

	[JsonPropertyName("baseVersionUtc")]
	public DateTime BaseVersionUtc { get; set; }
}
