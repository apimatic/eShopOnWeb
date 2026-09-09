using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record ChjsTokenizationSuccess
{
    [JsonPropertyName("payment_profile")]
    public required TokenizedPaymentProfile PaymentProfile { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("gateway_customer_id")]
    public int? GatewayCustomerId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
