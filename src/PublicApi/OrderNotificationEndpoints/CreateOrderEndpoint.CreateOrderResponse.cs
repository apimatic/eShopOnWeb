using System;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateOrderResponse()
    {
    }

    /// <summary>Identifier of the order just placed.</summary>
    public int OrderId { get; set; }

    public OrderSummaryDto Order { get; set; } = new();
}
