using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RefundOrderResponse()
    {
    }

    public int OrderId { get; set; }

    public Guid? RefundId { get; set; }

    /// <summary>Refunded, Existing, InProgress, Rejected, Unknown, ProviderUnavailable, NotPaid, NotFound, Invalid.</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>The order's payment state after this request, when known.</summary>
    public string? PaymentStatus { get; set; }

    /// <summary>What is still refundable on the order after this request, when known.</summary>
    public decimal? RemainingRefundable { get; set; }

    public string? Message { get; set; }

    public RefundDto? Refund { get; set; }
}
