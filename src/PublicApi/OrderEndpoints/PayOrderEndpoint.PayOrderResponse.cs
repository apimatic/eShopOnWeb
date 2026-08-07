using System;
using Microsoft.eShopWeb.PublicApi.PaymentShared;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }

    public PayOrderResponse() { }

    public int OrderId { get; set; }

    /// <summary>Payment lifecycle after the call, e.g. <c>Paid</c>.</summary>
    public string PaymentStatus { get; set; } = string.Empty;

    public OrderDto? Order { get; set; }
}
