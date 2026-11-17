using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Amount to refund in currency units. Omit to refund the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public string? FinixRefundId { get; set; }
    public decimal Amount { get; set; }
    public long AmountMinor { get; set; }
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
