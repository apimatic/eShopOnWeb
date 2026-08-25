using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Setup Token Request where the <c>source</c> defines the type of instrument to be stored.
/// </summary>
public record SetupTokenRequest
{
    /// <summary>
    /// This object defines a customer in your system. Use it to manage customer profiles, save payment methods and contact details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer")]
    public Customer? Customer { get; init; }

    /// <summary>
    /// The payment method to vault with the instrument details.
    /// </summary>
    [JsonPropertyName("payment_source")]
    public required SetupTokenRequestPaymentSource PaymentSource { get; init; }
}
