namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Re-send a notification. The idempotency key may be supplied in this body or in an
/// <c>Idempotency-Key</c> request header; repeating a request under the same key does not send again.
/// </summary>
public class ResendNotificationRequest
{
    public string? IdempotencyKey { get; set; }
}

/// <summary>The resend result; <c>notificationId</c> is the identifier of the message the resend produced.</summary>
public class ResendNotificationResponse
{
    public int NotificationId { get; set; }

    /// <summary>True when the key had already been used, so no new message was sent.</summary>
    public bool Replayed { get; set; }
}
