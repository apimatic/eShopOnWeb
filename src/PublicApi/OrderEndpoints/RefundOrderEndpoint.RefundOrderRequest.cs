using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a captured payment, in full (amount omitted) or in part. The idempotencyKey
/// makes a repeated request safe: it returns the original refund instead of refunding twice.
/// </summary>
public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Partial amount; omit to refund the remaining captured amount in full.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key deduplicating this refund request.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
