using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateOrderResponse()
    {
    }

    /// <summary>The new order's id (top-level, so callers can drive the pay/refund flow).</summary>
    public int OrderId { get; set; }

    public OrderDto Order { get; set; } = new();
}
