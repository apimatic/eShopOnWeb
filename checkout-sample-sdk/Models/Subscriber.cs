using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The subscriber response information.
/// </summary>
public record Subscriber
{
    /// <summary>
    /// The name of the party.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("name")]
    public Name? Name { get; init; }

    /// <summary>
    /// The shipping details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("shipping_address")]
    public ShippingDetails? ShippingAddress { get; init; }

    /// <summary>
    /// The payment source used to fund the payment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_source")]
    public SubscriptionPaymentSourceResponse? PaymentSource { get; init; }
}
