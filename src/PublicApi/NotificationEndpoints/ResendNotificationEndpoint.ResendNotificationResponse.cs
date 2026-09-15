using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }

    public ResendNotificationResponse() { }

    /// <summary>Identifier of the message the re-send produced (or the original, if the key was reused).</summary>
    public int NotificationId { get; set; }

    /// <summary>True when the idempotency key was already used and nothing new was sent.</summary>
    public bool Deduplicated { get; set; }
}
