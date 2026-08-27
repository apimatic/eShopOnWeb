using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderRequest : BaseRequest
{
    public DispatchOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class DispatchOrderResponse : BaseResponse
{
    public DispatchOrderResponse(Guid correlationId) : base(correlationId) { }
    public DispatchOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
