using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk.Models;

/// <summary>
/// The Balance information.
/// </summary>
public record BalanceInformation
{
    /// <summary>
    /// The <see href="/docs/integration/direct/rest/currency-codes/">three-character ISO-4217 currency code</see> that identifies the currency.
    /// </summary>
    [JsonPropertyName("currency")]
    [StringLength(3, MinimumLength = 3)]
    public required string Currency { get; init; }

    /// <summary>
    /// Optional field representing if the currency is primary currency or not.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("primary")]
    public bool? Primary { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonPropertyName("total_balance")]
    public required Money TotalBalance { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("available_balance")]
    public Money? AvailableBalance { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("withheld_balance")]
    public Money? WithheldBalance { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
