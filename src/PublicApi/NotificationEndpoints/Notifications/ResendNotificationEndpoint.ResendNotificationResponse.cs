using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints.Notifications;

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ResendNotificationResponse()
    {
    }

    /// <summary>The identifier of the message the resend produced (the same one on a repeated request).</summary>
    public int NotificationId { get; set; }
}
