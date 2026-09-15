using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Identifier of the message the resend produced (top-level). Stable across repeats of the same key.</summary>
    public int NotificationId { get; set; }

    /// <summary>The source notification that was re-sent.</summary>
    public int SourceNotificationId { get; set; }

    public string DeliveryStatus { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}
