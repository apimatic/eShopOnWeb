using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>The identifier of the notification the resend produced.</summary>
    public int NotificationId { get; set; }
    public string? MessageSid { get; set; }
    public string? Status { get; set; }
    public bool IdempotentReplay { get; set; }
}
