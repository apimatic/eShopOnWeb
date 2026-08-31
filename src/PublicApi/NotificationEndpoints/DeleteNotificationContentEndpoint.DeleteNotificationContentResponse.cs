using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DeleteNotificationContentResponse : BaseResponse
{
    public DeleteNotificationContentResponse(Guid correlationId) : base(correlationId)
    {
    }

    public DeleteNotificationContentResponse()
    {
    }

    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
}
