using Maxio.Models;

namespace Maxio.Services;

/// <summary>
/// Orchestrates the subscription lifecycle against Maxio Advanced Billing on behalf of an eShopOnWeb user.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans (products) available in the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<Product>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given user to the plan identified by <paramref name="planHandle"/>.
    /// Ensures a Maxio customer exists for the user (idempotent) and never creates a duplicate
    /// subscription for the same plan. Returns the resulting (or existing) subscription.
    /// </summary>
    Task<Subscription> SubscribeAsync(string userEmail, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the given user's Maxio customer.
    /// </summary>
    Task<IReadOnlyList<Subscription>> GetMySubscriptionsAsync(string userEmail, CancellationToken cancellationToken = default);
}
