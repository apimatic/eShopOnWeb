using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>
    /// Caller-supplied idempotency key. Repeating the request under the same key returns the
    /// notification the first attempt produced, without sending a second message.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ResendNotificationResponse()
    {
    }

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public int ResendOfId { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ProviderStatus { get; set; }
    public bool AlreadyExisted { get; set; }
}
