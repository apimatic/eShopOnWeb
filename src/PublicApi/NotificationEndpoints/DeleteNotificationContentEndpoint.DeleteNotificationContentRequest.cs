using System;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DeleteNotificationContentRequest : BaseRequest
{
    [FromRoute(Name = "notificationId")]
    public int NotificationId { get; set; }
}

public class DeleteNotificationContentResponse : BaseResponse
{
    public DeleteNotificationContentResponse(Guid correlationId) : base(correlationId) {}
    public DeleteNotificationContentResponse() {}

    public int NotificationId { get; set; }
}
