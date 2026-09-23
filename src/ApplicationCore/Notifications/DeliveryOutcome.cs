namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// A coarse, caller-facing classification of a message's provider status. Provider statuses that mean
/// "the provider has not finished" are <see cref="Pending"/>; an absent or unrecognized status is
/// <see cref="Unknown"/> — neither is ever reported as delivered.
/// </summary>
public enum DeliveryOutcome
{
    Pending,
    Delivered,
    Failed,
    Scheduled,
    Canceled,
    Disposed,
    Unknown
}

public static class DeliveryOutcomeMapper
{
    /// <summary>
    /// Sorts a Twilio message status wire value into done / not-yet / failed. Never coalesces an absent
    /// status to success: null or an unrecognized value is <see cref="DeliveryOutcome.Unknown"/> (not-yet).
    /// </summary>
    public static DeliveryOutcome FromProviderStatus(string? status, bool contentDisposed = false)
    {
        if (contentDisposed) return DeliveryOutcome.Disposed;

        return status switch
        {
            "delivered" or "received" or "read" => DeliveryOutcome.Delivered,
            "failed" or "undelivered" or "partially_delivered" => DeliveryOutcome.Failed,
            "canceled" => DeliveryOutcome.Canceled,
            "scheduled" => DeliveryOutcome.Scheduled,
            "queued" or "sending" or "sent" or "accepted" or "receiving" => DeliveryOutcome.Pending,
            _ => DeliveryOutcome.Unknown
        };
    }

    /// <summary>Terminal states a message can never leave — used to skip unnecessary provider refreshes.</summary>
    public static bool IsTerminal(DeliveryOutcome outcome) =>
        outcome is DeliveryOutcome.Delivered or DeliveryOutcome.Failed
            or DeliveryOutcome.Canceled or DeliveryOutcome.Disposed;

    /// <summary>A message the shopper did not receive and that an operator may legitimately re-send.</summary>
    public static bool IsResendable(DeliveryOutcome outcome) => outcome is DeliveryOutcome.Failed;
}
