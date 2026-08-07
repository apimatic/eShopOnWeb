using System;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    /// <summary>The new order's id (top-level, so a caller can drive the pay/refund flow).</summary>
    public int OrderId { get; set; }

    public OrderSummaryDto? Order { get; set; }
}
