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

    /// <summary>Top-level identifier of the refund.</summary>
    public string RefundId { get; set; } = string.Empty;

    public decimal RefundAmount { get; set; }
    public string RefundStatus { get; set; } = string.Empty;
    public PaymentStateDto Payment { get; set; } = new();
}
