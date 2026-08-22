using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class RedactNotificationContentRequest : BaseRequest
{
    public RedactNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; set; }
}

public class RedactNotificationContentResponse : BaseResponse
{
    public RedactNotificationContentResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RedactNotificationContentResponse()
    {
    }

    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
}
