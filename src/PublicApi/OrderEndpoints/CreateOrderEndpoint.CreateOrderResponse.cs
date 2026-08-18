using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }

    public CreateOrderResponse() { }

    /// <summary>The identifier of the newly placed order (top-level, so dispatch/cancel can be driven onward).</summary>
    public int OrderId { get; set; }

    public decimal Total { get; set; }
}
