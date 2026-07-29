using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>Identifies the eShopOnWeb user that a billing operation acts on behalf of.</summary>
/// <param name="Reference">Stable unique identifier used as the Maxio customer <c>reference</c> (the eShop user id).</param>
/// <param name="Email">The user's email address.</param>
public readonly record struct BillingUser(string Reference, string Email);

/// <summary>Outcome of a subscribe operation.</summary>
/// <param name="Subscription">The active Maxio subscription.</param>
/// <param name="AlreadyExisted">
/// True when an equivalent active subscription already existed and no new one was created — i.e. the
/// operation was an idempotent no-op (e.g. a double-clicked subscribe).
/// </param>
public readonly record struct SubscribeResult(MaxioSubscription Subscription, bool AlreadyExisted);

/// <summary>
/// Orchestrates the eShopOnWeb subscription-billing flows on top of <see cref="IMaxioClient"/>, adding the
/// idempotency guarantees the hero "Subscribe" flow requires.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the active (non-archived) plans available in the configured product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="user"/> and that they are enrolled in the plan
    /// identified by <paramref name="planHandle"/>. Idempotent: a repeated call (or a double-click) neither
    /// creates a second customer nor a second subscription.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(BillingUser user, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subscriptions belonging to <paramref name="user"/>. Returns an empty list (without
    /// creating anything) when the user has never been enrolled.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsAsync(BillingUser user, CancellationToken cancellationToken = default);
}
