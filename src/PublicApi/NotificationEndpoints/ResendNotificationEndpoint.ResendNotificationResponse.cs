using System;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) {}
    public ResendNotificationResponse() {}

    /// <summary>Identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public OrderNotificationDto? Notification { get; set; }
    public string? Message { get; set; }
}
