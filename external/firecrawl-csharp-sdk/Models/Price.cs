using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// The current price of the variant.
/// </summary>
public record Price
{
    /// <summary>
    /// The numeric price amount.
    /// </summary>
    [JsonPropertyName("amount")]
    public required double Amount { get; init; }

    /// <summary>
    /// The ISO 4217 currency code (e.g. 'USD').
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    /// <summary>
    /// The price formatted for display (e.g. '$199.99').
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("formatted")]
    public string? Formatted { get; init; }
}
