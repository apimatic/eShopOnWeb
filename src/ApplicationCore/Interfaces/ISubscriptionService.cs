using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing for eShopOnWeb, backed by Maxio Advanced Billing. This capability
/// is additive and parallel to the existing one-time commerce flow (Catalog → Basket → Order).
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the plans a shopper can subscribe to (the products in the configured Maxio product family).
    /// </summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given user to a plan. Ensures a Maxio customer exists for the user (keyed on a
    /// stable reference derived from <paramref name="userName"/>) and enrolls them. The operation is
    /// idempotent: a repeated call for a plan the user already has a live subscription to returns the
    /// existing subscription instead of creating a duplicate.
    /// </summary>
    /// <param name="userName">The authenticated user's identity (their eShopOnWeb user name / email).</param>
    /// <param name="planHandle">The stable handle of the plan to subscribe to (e.g. "eshop-pro").</param>
    Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the given user. Returns an empty collection if the user has
    /// never subscribed (i.e. has no Maxio customer record yet).
    /// </summary>
    Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}
