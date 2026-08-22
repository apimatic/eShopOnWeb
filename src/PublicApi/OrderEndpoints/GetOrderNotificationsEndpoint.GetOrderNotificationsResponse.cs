using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetOrderNotificationsResponse : BaseResponse
{
    public GetOrderNotificationsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetOrderNotificationsResponse()
    {
    }

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
