using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models.Enums;

namespace MaxioAdvancedBilling.Models;

public record CreatePrepayment
{
    [JsonPropertyName("amount")]
    public required double Amount { get; init; }

    [JsonPropertyName("details")]
    public required string Details { get; init; }

    [JsonPropertyName("memo")]
    public required string Memo { get; init; }

    /// <summary>
    /// When the <c>method</c> specified is <c>"credit_card_on_file"</c>, the prepayment amount will be collected using the default credit card payment profile and applied to the prepayment account balance. This is especially useful for manual replenishment of prepaid subscriptions.
    /// </summary>
    [JsonPropertyName("method")]
    public required CreatePrepaymentMethod Method { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_profile_id")]
    public int? PaymentProfileId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
