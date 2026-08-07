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

    /// <summary>Identifier of the newly placed order (top-level, so callers can drive the flow).</summary>
    public int OrderId { get; set; }

    public OrderSummaryDto Order { get; set; } = new();
}
