using System.Text.Json.Serialization;
using PayPal.Core.Models;

namespace PayPal.Models;

/// <summary>
/// The payment source used to fund the payment.
/// </summary>
public record SubscriptionPaymentSourceResponse
{
    /// <summary>
    /// The payment card used to fund the payment. Card can be a credit or debit card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("card")]
    public CardResponseWithBillingAddress? Card { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
