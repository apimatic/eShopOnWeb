using System;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }

    public PlaceOrderResponse() { }

    /// <summary>Identifier of the newly placed order (top-level, so the flow can be driven end to end).</summary>
    public int OrderId { get; set; }

    public OrderSummaryDto Order { get; set; } = new();
}
