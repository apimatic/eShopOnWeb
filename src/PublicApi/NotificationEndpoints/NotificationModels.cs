namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest
{
    /// <summary>
    /// Caller-supplied idempotency key. Repeating a resend under the same key does not send a
    /// second message; a fresh key is a genuine new attempt. May also be supplied via the
    /// <c>Idempotency-Key</c> request header.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse
{
    /// <summary>Identifier of the message the resend produced (top-level, drives the flow onward).</summary>
    public int NotificationId { get; set; }

    /// <summary>True when this key was already used, so no new message was sent.</summary>
    public bool Duplicate { get; set; }
}
