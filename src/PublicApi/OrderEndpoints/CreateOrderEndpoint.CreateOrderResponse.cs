using System;
using Microsoft.eShopWeb.PublicApi.PaymentShared;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }

    public CreateOrderResponse() { }

    /// <summary>The new order's identifier, exposed as a top-level field so the flow can be driven end to end.</summary>
    public int OrderId { get; set; }

    public OrderDto? Order { get; set; }
}
