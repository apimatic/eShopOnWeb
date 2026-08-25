using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by the external billing system of record (Maxio Advanced Billing).
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans available in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes a user to a plan. Idempotent: ensures the billing customer exists (created at most
    /// once per user) and returns the existing live subscription if the user is already subscribed
    /// to the same plan instead of creating a duplicate.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions the user has in the billing system.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string userId, string email, CancellationToken cancellationToken = default);
}
