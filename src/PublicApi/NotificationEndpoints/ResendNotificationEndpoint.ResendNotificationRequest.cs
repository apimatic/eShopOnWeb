using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>The JSON body of a resend request.</summary>
public class ResendNotificationBody
{
    /// <summary>Caller-supplied idempotency key. A repeat under the same key does not resend.</summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; init; }
    public string? IdempotencyKey { get; init; }

    public ResendNotificationRequest(int notificationId, string? idempotencyKey)
    {
        NotificationId = notificationId;
        IdempotencyKey = idempotencyKey;
    }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ResendNotificationResponse()
    {
    }

    /// <summary>The identifier of the message the resend produced (top-level).</summary>
    public int NotificationId { get; set; }

    public string Status { get; set; } = string.Empty;
}
