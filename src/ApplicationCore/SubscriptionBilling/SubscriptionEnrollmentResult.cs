namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// Outcome of an idempotent subscribe operation. A repeated subscribe for a plan the
/// user already holds returns the existing subscription with <see cref="WasCreated"/> set to false.
/// </summary>
public class SubscriptionEnrollmentResult
{
    public required SubscriptionDetails Subscription { get; init; }
    public bool WasCreated { get; init; }
}
