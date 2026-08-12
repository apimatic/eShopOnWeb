namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Body for a resend. The idempotency key may also be supplied via the <c>Idempotency-Key</c> header;
/// the body value takes precedence when both are present.
/// </summary>
public class ResendNotificationRequest
{
    public string? IdempotencyKey { get; set; }
}

/// <summary>Response for a resend. Returns the produced message's id as top-level <c>notificationId</c>.</summary>
public class ResendNotificationResponse
{
    public int NotificationId { get; set; }

    /// <summary>True when this call replayed a prior result under the same key rather than sending again.</summary>
    public bool Replayed { get; set; }
}
