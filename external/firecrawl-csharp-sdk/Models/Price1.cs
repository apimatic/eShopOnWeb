using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// The price of the item.
/// </summary>
public record Price1
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
    /// The price formatted for display (e.g. '$7.99').
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("formatted")]
    public string? Formatted { get; init; }
}
