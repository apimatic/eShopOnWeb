using System;
using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }
    public PlaceOrderResponse() { }

    /// <summary>The created order's id (top-level, so callers can drive the pay/refund flow).</summary>
    public int OrderId { get; set; }

    public OrderDto? Order { get; set; }
}
