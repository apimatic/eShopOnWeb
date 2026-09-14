using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring subscription billing, backed by an external billing system of record.
/// </summary>
/// <remarks>
/// This sits alongside -- and is independent of -- the one-time Catalog/Basket/Order flow.
/// The billing system, not eShopOnWeb, owns subscription state; every read goes to it.
/// </remarks>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the plans currently on offer, cheapest first.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a user in a plan, creating the billing customer record on first use.
    /// </summary>
    /// <remarks>
    /// Idempotent per (user, plan): if the user already holds a live subscription to the plan, that
    /// subscription is returned with <see cref="SubscribeResult.AlreadySubscribed"/> set instead of a
    /// second one being created.
    /// </remarks>
    /// <exception cref="Exceptions.SubscriptionPlanNotFoundException">The requested plan is not on offer.</exception>
    /// <exception cref="Exceptions.BillingProviderException">The billing system rejected or failed the request.</exception>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription held by a user, newest first. Returns empty when the user has never subscribed.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}
