using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to give back, in the order's currency. Omit to refund everything still refundable.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Optional free-text note recorded with the refund.</summary>
    public string? Reason { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>From the optional <c>Idempotency-Key</c> request header: repeating a request with the same key never refunds twice.</summary>
    [JsonIgnore]
    public string? IdempotencyKey { get; set; }

    [JsonIgnore]
    public string RequestedBy { get; set; } = string.Empty;
}
