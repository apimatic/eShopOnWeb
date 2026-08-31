using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    [JsonIgnore]
    public int NotificationId { get; set; }

    /// <summary>Caller-supplied key making a repeated resend request safe.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }

    /// <summary>True when the idempotency key was already used and no second message was sent.</summary>
    public bool WasDuplicate { get; set; }
}
