using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DisposeNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; init; }

    public DisposeNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }
}

public class DisposeNotificationContentResponse : BaseResponse
{
    public DisposeNotificationContentResponse(Guid correlationId) : base(correlationId)
    {
    }

    public DisposeNotificationContentResponse()
    {
    }

    public int NotificationId { get; set; }
    public bool ContentDisposed { get; set; } = true;
}
