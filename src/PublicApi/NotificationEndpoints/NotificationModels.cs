namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest
{
    /// <summary>
    /// Caller-supplied idempotency key. Repeating a resend under the same key does not send a
    /// second message; a fresh key is a genuine new attempt.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse
{
    public ResendNotificationResponse(int notificationId)
    {
        NotificationId = notificationId;
    }

    /// <summary>Identifier of the message the resend produced (top-level).</summary>
    public int NotificationId { get; set; }
}
