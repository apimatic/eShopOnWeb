using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;

namespace PayPal.Models;

/// <summary>
/// The store information.
/// </summary>
public record StoreInformation
{
    /// <summary>
    /// The ID of a store for a merchant in the system of record.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("store_id")]
    [StringLength(100, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9]*$")]
    public string? StoreId { get; init; }

    /// <summary>
    /// The terminal ID for the checkout stand in a merchant store.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("terminal_id")]
    [StringLength(60, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9]*$")]
    public string? TerminalId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
