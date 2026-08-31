using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) {}
    public ResendNotificationResponse() {}

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }

    /// <summary>True when the idempotency key was already used — no second message was sent.</summary>
    public bool IdempotentReplay { get; set; }

    public string Status { get; set; } = string.Empty;
}
