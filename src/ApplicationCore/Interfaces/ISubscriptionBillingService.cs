using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>List the plans available for subscription.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribe a user to a plan. Idempotent per user: ensures the Maxio customer exists
    /// (keyed on the username) and returns the existing subscription when the user already
    /// holds a live one for the same plan.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(string username, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>List the user's subscriptions; empty when the user has no Maxio customer yet.</summary>
    Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(string username, CancellationToken cancellationToken = default);
}
