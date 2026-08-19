using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Merchant-specific identifiers for the item.
/// </summary>
public record Identifiers
{
    /// <summary>
    /// The merchant's own item ID.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("merchantItemId")]
    public string? MerchantItemId { get; init; }
}
