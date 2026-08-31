using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetOrderNotificationsRequest : CancellableRequest
{
    public GetOrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; init; }
    public string? BuyerId { get; set; }
}

public class GetOrderNotificationsResponse : BaseResponse
{
    public GetOrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public GetOrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
