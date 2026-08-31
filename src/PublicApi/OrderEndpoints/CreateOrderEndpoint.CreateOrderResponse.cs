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

    /// <summary>The identifier of the placed order (top-level, so the flow can be driven onward).</summary>
    public int OrderId { get; set; }

    public decimal Total { get; set; }

    public int ItemCount { get; set; }
}
