using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record ChjsTokenizationFailure
{
    [JsonPropertyName("errors")]
    public required string Errors { get; init; }

    /// <summary>
    /// PCI-safe cardholder fields only. Full card numbers, CVV, and billing address are never included.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_profile_params")]
    public PaymentProfileParams? PaymentProfileParams { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
