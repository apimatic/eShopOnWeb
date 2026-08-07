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

    /// <summary>Top-level identifier of the created order, so the flow can be driven end to end.</summary>
    public int OrderId { get; set; }

    public OrderDto Order { get; set; } = new();
}
