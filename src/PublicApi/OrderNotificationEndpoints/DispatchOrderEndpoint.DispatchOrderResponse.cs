using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class DispatchOrderResponse : BaseResponse
{
    public DispatchOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public DispatchOrderResponse()
    {
    }

    public int OrderId { get; set; }

    public List<NotificationDto> Notifications { get; set; } = new();
}
