using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : CancellableRequest
{
    /// <summary>Caller-supplied idempotency key; repeating the same key never sends twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonIgnore]
    public int NotificationId { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }

    public int? ResendOfNotificationId { get; set; }
    public string? Status { get; set; }
    public string? ProviderMessageSid { get; set; }

    /// <summary>True when the idempotency key was already consumed; no second message was sent.</summary>
    public bool Duplicate { get; set; }

    public string? Error { get; set; }
}
