using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class OrderNotificationsResponse : BaseResponse
{
    public OrderNotificationsResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new List<NotificationDto>();
}
