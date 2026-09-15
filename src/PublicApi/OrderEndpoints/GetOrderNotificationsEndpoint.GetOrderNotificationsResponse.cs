using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; init; }
}

public class GetOrderNotificationsResponse : BaseResponse
{
    public GetOrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public GetOrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
