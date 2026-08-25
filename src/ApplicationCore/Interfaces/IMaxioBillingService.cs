using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Gateway to Maxio Advanced Billing, the billing system of record for subscriptions.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the subscription plans (products) available in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently subscribes a shopper to a plan: ensures a Maxio customer exists for the
    /// given eShop user (keyed by <paramref name="userId"/> as the customer reference) and
    /// creates the subscription. If a live subscription to the same plan already exists,
    /// it is returned instead of creating a duplicate.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(string userId, string email, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the shopper's subscriptions. Returns an empty list when no Maxio customer
    /// exists yet for the user (a customer record is never created on a read).
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string userId, string email, CancellationToken cancellationToken = default);
}
