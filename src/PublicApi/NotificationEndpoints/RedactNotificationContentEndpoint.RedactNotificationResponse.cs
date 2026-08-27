using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class RedactNotificationRequest : BaseRequest
{
    public int NotificationId { get; init; }

    public RedactNotificationRequest(int notificationId)
    {
        NotificationId = notificationId;
    }
}

public class RedactNotificationResponse : BaseResponse
{
    public RedactNotificationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RedactNotificationResponse()
    {
    }

    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
}
