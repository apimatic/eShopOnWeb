using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateOrderResponse()
    {
    }

    /// <summary>The identifier of the placed order, so the flow can be driven end to end.</summary>
    public int OrderId { get; set; }

    public decimal Total { get; set; }

    public DateTimeOffset OrderDate { get; set; }

    public List<OrderLineDto> Items { get; set; } = new();
}
