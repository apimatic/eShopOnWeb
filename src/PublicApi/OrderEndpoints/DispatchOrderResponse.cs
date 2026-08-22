using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderActionResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class DispatchOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    public DispatchOrderRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class DispatchOrderResponse : OrderActionResponse
{
    public DispatchOrderResponse(Guid correlationId) : base()
    {
        _correlationId = correlationId;
    }
}
