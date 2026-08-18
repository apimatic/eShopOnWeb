using System;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ResendNotificationResponse()
    {
    }

    /// <summary>Identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }

    public NotificationDto Notification { get; set; } = new();
}
