namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Body for a resend. The caller-supplied idempotency key makes repeats safe.</summary>
public record ResendNotificationRequest(string IdempotencyKey);

// Commands used by the endpoint HandleAsync methods.
public record ResendNotificationCommand(int NotificationId, string IdempotencyKey);
public record DisposeContentCommand(int NotificationId);
public record ReconciliationQuery(string From, string To);

public class ResendNotificationResponse
{
    /// <summary>The identifier of the message this resend produced.</summary>
    public int NotificationId { get; set; }

    /// <summary>True if a new message was sent; false if a prior request under the same key was
    /// reused (no second message).</summary>
    public bool Resent { get; set; }
}
