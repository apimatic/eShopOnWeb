namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public record ResendNotificationRequest(string? IdempotencyKey);

/// <summary>
/// Response to an operator re-send. <see cref="NotificationId"/> is the identifier of the message the
/// re-send produced (top-level).
/// </summary>
public class ResendNotificationResponse
{
    public int NotificationId { get; set; }

    /// <summary>True when the idempotency key had already been used, so no new message was sent.</summary>
    public bool Reused { get; set; }

    public string Status { get; set; } = string.Empty;
}
