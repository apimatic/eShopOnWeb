using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse() { }

    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>Top-level identifier of the created resource (the PayPal refund id).</summary>
    public string RefundId { get; set; } = string.Empty;

    public int OrderId { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public decimal? TotalRefundedAmount { get; set; }

    public decimal RemainingRefundableAmount { get; set; }

    public bool Replayed { get; set; }
}
