using System;
using Microsoft.eShopWeb.PublicApi.PaymentShared;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }

    public RefundOrderResponse() { }

    public int OrderId { get; set; }

    /// <summary>Payment lifecycle after the refund, i.e. <c>Refunded</c>.</summary>
    public string PaymentStatus { get; set; } = string.Empty;

    public string? PayPalRefundId { get; set; }

    public OrderDto? Order { get; set; }
}
