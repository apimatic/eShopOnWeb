namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Optional body of POST /api/notifications/{notificationId}/resend.</summary>
public class ResendRequest
{
    /// <summary>The caller-supplied idempotency key (may also be supplied via the Idempotency-Key header).</summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>Response of POST /api/notifications/{notificationId}/resend. Carries the produced message's id.</summary>
public class ResendResponse
{
    /// <summary>The notificationId of the message the re-send produced.</summary>
    public int NotificationId { get; set; }

    /// <summary>True when this key was already used and no new message was sent.</summary>
    public bool Deduplicated { get; set; }
}
