using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListOrderNotificationsResponse()
    {
    }

    public List<NotificationDto> Notifications { get; set; } = new();
}
