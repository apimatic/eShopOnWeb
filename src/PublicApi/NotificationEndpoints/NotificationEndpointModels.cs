namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Body for re-sending a message that did not reach the shopper.</summary>
public class ResendNotificationRequest
{
    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key does not send a
    /// second message; a genuine second attempt uses a fresh key.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>Response for a re-send; carries the id of the notification the re-send produced.</summary>
public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
}
