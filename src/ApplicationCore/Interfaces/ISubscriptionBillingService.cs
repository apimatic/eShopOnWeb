using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external system of record. eShopOnWeb stores no
/// subscription state of its own: every read goes to the provider so the answer is always current.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Plans a shopper may subscribe to, newest catalog state.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrols the user in a plan. Idempotent: repeating the call never yields a second billing
    /// customer or a second live subscription to the same plan.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Every subscription belonging to the user, including ended ones.</summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default);
}
