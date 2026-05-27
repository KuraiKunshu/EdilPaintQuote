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
	Rifiutato
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

	// Metadati di sincronizzazione
	[JsonPropertyName("lastModifiedUtc")]
	public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;
	
	[JsonPropertyName("syncHash")]
	public string SyncHash { get; set; } = string.Empty;
}