using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DeleteNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; init; }

    public DeleteNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }
}

public class DeleteNotificationContentResponse : BaseResponse
{
    public DeleteNotificationContentResponse(Guid correlationId) : base(correlationId) { }

    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
    public string Status { get; set; } = string.Empty;
}
