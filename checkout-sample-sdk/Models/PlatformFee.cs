using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The platform or partner fee, commission, or brokerage fee that is associated with the transaction. Not a separate or isolated transaction leg from the external perspective. The platform fee is limited in scope and is always associated with the original payment for the purchase unit.
/// </summary>
public record PlatformFee
{
    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonPropertyName("amount")]
    public required Money Amount { get; init; }

    /// <summary>
    /// The details for the merchant who receives the funds and fulfills the order. The merchant is also known as the payee.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payee")]
    public PayeeBase? Payee { get; init; }
}
