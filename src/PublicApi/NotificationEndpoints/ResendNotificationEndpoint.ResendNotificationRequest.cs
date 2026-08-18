using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>Caller-supplied idempotency key. May also be supplied via the 'Idempotency-Key' header.</summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }

    public ResendNotificationResponse() { }

    /// <summary>Top-level identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }

    public string? Status { get; set; }
    public string? ProviderMessageSid { get; set; }
}
