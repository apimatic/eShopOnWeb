namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a <see cref="Interfaces.ISubscriptionBillingService.SubscribeAsync"/> call.
/// </summary>
public record SubscribeResult
{
    /// <summary>The subscription the user is enrolled in.</summary>
    public required CustomerSubscription Subscription { get; init; }

    /// <summary>
    /// True when an equivalent live subscription already existed and was reused instead of
    /// creating a new one (idempotent enrollment — e.g. a double-clicked subscribe).
    /// </summary>
    public bool AlreadyExisted { get; init; }
}
