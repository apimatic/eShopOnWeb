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

    /// <summary>Top-level identifier of the created order.</summary>
    public int OrderId { get; set; }

    public PaymentStateDto Payment { get; set; } = new();
}
