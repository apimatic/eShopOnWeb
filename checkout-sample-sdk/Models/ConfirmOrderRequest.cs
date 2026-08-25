using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Payer confirms the intent to pay for the Order using the provided payment source.
/// </summary>
public record ConfirmOrderRequest
{
    /// <summary>
    /// The payment source definition.
    /// </summary>
    [JsonPropertyName("payment_source")]
    public required PaymentSource PaymentSource { get; init; }

    /// <summary>
    /// Customizes the payer confirmation experience.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("application_context")]
    public OrderConfirmApplicationContext? ApplicationContext { get; init; }
}
