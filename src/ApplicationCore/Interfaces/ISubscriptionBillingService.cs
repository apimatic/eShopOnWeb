using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing. The billing provider is the system of record for plans,
/// customers and subscriptions; this application only keeps the claims that stop a user from
/// being enrolled twice.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>The plans a shopper may subscribe to.</summary>
    Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for <paramref name="subscriber"/> and enrolls them in
    /// <paramref name="planHandle"/>. Safe to call twice: a repeat returns the existing subscription.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>The subscriptions held by <paramref name="userName"/>; empty when they have never subscribed.</summary>
    Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}
