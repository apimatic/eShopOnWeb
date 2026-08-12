using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Identifier of the message the resend produced (or the existing one on an idempotent replay).</summary>
    public int NotificationId { get; set; }

    /// <summary>True when a new message was sent; false when the request was an idempotent replay.</summary>
    public bool Sent { get; set; }
}
