using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds the captured payment in full (<see cref="Amount"/> omitted) or in part.
/// <see cref="IdempotencyKey"/> is required: repeating the request under the same key returns
/// the original refund instead of refunding twice; a distinct key issues a distinct refund.
/// </summary>
public class RefundOrderRequest : BaseRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Set from the route by the endpoint; never accepted from the request body.</summary>
    [JsonIgnore]
    public int OrderId { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }

    /// <summary>True when this idempotency key was already processed — no second refund was issued.</summary>
    public bool Replayed { get; set; }

    public string PaymentStatus { get; set; } = string.Empty;
}
