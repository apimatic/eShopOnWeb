namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the subscription capability: browsing plans, subscribing (idempotently),
/// and listing a user's subscriptions. Maxio (Advanced Billing) is the system of record.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the subscription plans available in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the user (idempotent) and subscribes them to the
    /// given plan. If the user already has a live subscription to the plan, the existing
    /// subscription is returned instead of creating a duplicate.
    /// </summary>
    Task<SubscriptionResult> SubscribeAsync(string userKey, string email, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions belonging to the user's Maxio customer.</summary>
    Task<IReadOnlyList<SubscriptionResult>> GetMySubscriptionsAsync(string userKey, CancellationToken cancellationToken = default);
}
