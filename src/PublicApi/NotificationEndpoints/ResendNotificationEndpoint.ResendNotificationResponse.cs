using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) {}
    public ResendNotificationResponse() {}

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public int? ResendOfNotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
}
