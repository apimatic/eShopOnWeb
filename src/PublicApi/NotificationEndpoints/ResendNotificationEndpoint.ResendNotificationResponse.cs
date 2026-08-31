using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public int OriginalNotificationId { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>True when the idempotency key was already used and no new message was sent.</summary>
    public bool Replayed { get; set; }
}
