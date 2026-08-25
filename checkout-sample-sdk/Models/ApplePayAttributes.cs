using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Additional attributes associated with apple pay.
/// </summary>
public record ApplePayAttributes
{
    /// <summary>
    /// This object represents a merchant’s customer, allowing them to store contact details, and track all payments associated with the same customer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer")]
    public CustomerInformation? Customer { get; init; }

    /// <summary>
    /// Base vaulting specification. The object can be extended for specific use cases within each payment_source that supports vaulting.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("vault")]
    public VaultInstruction? Vault { get; init; }
}
