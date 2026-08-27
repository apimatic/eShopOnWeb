using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsRequest : BaseRequest
{
    [FromRoute(Name = "orderId")]
    public int OrderId { get; set; }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse(Guid correlationId) : base(correlationId) {}
    public ListOrderNotificationsResponse() {}

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
