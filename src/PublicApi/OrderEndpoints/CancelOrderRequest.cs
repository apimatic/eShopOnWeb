using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    public CancelOrderRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class CancelOrderResponse : OrderActionResponse
{
    public CancelOrderResponse(Guid correlationId) : base()
    {
        _correlationId = correlationId;
    }
}
