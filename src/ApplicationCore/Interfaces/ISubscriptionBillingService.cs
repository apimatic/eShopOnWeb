using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Subscription billing against the Maxio Advanced Billing provider.
/// The caller identity is the eShopOnWeb user id; a Maxio customer is ensured per user
/// (idempotent) and subscriptions are tracked per (user, plan).
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans the store offers (products of the configured Maxio product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for the user and subscribes them to the given plan handle.
    /// Idempotent: a repeated call for the same user and plan returns the existing subscription
    /// instead of creating a second one.
    /// </summary>
    /// <exception cref="MaxioBillingException">The provider rejected the request, was unreachable, or the plan is unknown.</exception>
    Task<SubscriptionInfo> SubscribeAsync(string eshopUserId, string email, string planHandle, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the subscriptions the user holds. Returns an empty list when the user has no
    /// Maxio customer yet (a read never creates one).
    /// </summary>
    Task<IReadOnlyList<SubscriptionInfo>> GetSubscriptionsForUserAsync(string eshopUserId, CancellationToken cancellationToken);
}