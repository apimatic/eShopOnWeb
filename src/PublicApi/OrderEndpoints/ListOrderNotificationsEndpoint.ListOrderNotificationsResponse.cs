using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse(Guid correlationId) : base(correlationId) {}
    public ListOrderNotificationsResponse() {}

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
