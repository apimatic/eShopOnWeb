using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record RefundSuccess
{
    [JsonPropertyName("refund_id")]
    public required int RefundId { get; init; }

    [JsonPropertyName("gateway_transaction_id")]
    public required int GatewayTransactionId { get; init; }

    [JsonPropertyName("product_id")]
    public required int ProductId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
