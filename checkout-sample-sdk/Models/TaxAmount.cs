using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The tax levied by a government on the purchase of goods or services.
/// </summary>
public record TaxAmount
{
    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tax_amount")]
    public Money? TaxAmountValue { get; init; }
}
