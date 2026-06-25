using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace EdilPaintPreventibiviGen.Models;

public class Customer : INotifyPropertyChanged
{
	private string _businessName = string.Empty;
	private string _address = string.Empty;
	private string _email = string.Empty;
	private string _phone = string.Empty;
	private double _materialDiscount;
	private double _laborDiscount;

	[JsonPropertyName("syncId")]
	public Guid SyncId { get; set; }

	[JsonPropertyName("Ragione Sociale")] 
	public string BusinessName { get => _businessName; set { _businessName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } }

	[JsonPropertyName("Indirizzo")]
	public string Address { get => _address; set { _address = value; OnPropertyChanged(); } }

	[JsonPropertyName("email")]
	public string Email { get => _email; set { _email = value; OnPropertyChanged(); } }

	[JsonPropertyName("Telefono")] 
	public string Phone { get => _phone; set { _phone = value; OnPropertyChanged(); } }

	[JsonPropertyName("sconto_materiale")]
	public double MaterialDiscount { get => _materialDiscount; set { _materialDiscount = value; OnPropertyChanged(); } }

	[JsonPropertyName("sconto_lavori")]
	public double LaborDiscount { get => _laborDiscount; set { _laborDiscount = value; OnPropertyChanged(); } }

	// Metadati di sincronizzazione
	[JsonPropertyName("lastModifiedUtc")]
	public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;

	[JsonPropertyName("baseVersionUtc")]
	public DateTime BaseVersionUtc { get; set; }

	[JsonPropertyName("hasPendingDatabaseWrite")]
	public bool HasPendingDatabaseWrite { get; set; }

	[JsonIgnore]
	public string DisplayName => BusinessName;

	public bool ContainsText(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return true;
		return BusinessName.Contains(text, StringComparison.OrdinalIgnoreCase)
			|| Address.Contains(text, StringComparison.OrdinalIgnoreCase)
			|| Email.Contains(text, StringComparison.OrdinalIgnoreCase)
			|| Phone.Contains(text, StringComparison.OrdinalIgnoreCase);
	}

	public event PropertyChangedEventHandler? PropertyChanged;
	protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
