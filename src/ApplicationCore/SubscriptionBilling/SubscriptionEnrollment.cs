namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// The outcome of a subscribe operation. <see cref="IsNew"/> is false when an
/// equivalent live subscription already exists and was returned instead of creating
/// a duplicate (idempotent replay).
/// </summary>
public sealed class SubscriptionEnrollment
{
    public bool IsNew { get; init; }

    public SubscriptionDetails Subscription { get; init; } = new SubscriptionDetails();
}
