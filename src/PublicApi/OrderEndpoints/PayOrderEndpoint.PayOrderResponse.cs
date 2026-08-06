using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public OrderDto Order { get; set; } = new();
}
