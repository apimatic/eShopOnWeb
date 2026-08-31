using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Partial amount to refund; omit to refund the remaining captured amount in full.</summary>
    public decimal? Amount { get; set; }

    public string? NoteToPayer { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key returns
    /// the original refund instead of refunding twice; a new key issues a new refund.
    /// </summary>
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
}
