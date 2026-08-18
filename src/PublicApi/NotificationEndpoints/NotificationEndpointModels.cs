using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Response to a resend. The identifier of the message the resend produced is a top-level field.</summary>
public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The notification (message) the resend produced.</summary>
    public int NotificationId { get; set; }

    public string DeliveryOutcome { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}
