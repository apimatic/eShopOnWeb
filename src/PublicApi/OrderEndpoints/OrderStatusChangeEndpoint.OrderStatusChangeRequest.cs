using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderStatusChangeRequest : BaseRequest
{
    public int OrderId { get; init; }

    public OrderStatusChangeRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class OrderStatusChangeResponse : BaseResponse
{
    public OrderStatusChangeResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
