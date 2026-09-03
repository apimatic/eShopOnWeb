using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class RedactNotificationContentResponse : BaseResponse
{
    public RedactNotificationContentResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RedactNotificationContentResponse()
    {
    }

    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; } = true;
}
