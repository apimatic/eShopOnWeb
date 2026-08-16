namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The app's own view of a single refund's outcome, mapped from PayPal's refund status.
/// </summary>
public enum RefundStatus
{
    /// <summary>Recorded locally, not yet confirmed completed by PayPal.</summary>
    Pending = 0,

    /// <summary>PayPal confirmed the refund completed.</summary>
    Completed = 1,

    /// <summary>PayPal reported the refund failed.</summary>
    Failed = 2,

    /// <summary>PayPal reported the refund was cancelled.</summary>
    Cancelled = 3
}
