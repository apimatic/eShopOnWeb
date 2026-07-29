using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record
/// (Maxio Advanced Billing). This is an additive, parallel capability to the
/// existing one-time commerce flow and never mutates the local catalog/order data.
/// </summary>
public interface IBillingService
{
    /// <summary>
    /// Lists the subscription plans available for sign-up (the products belonging to the
    /// configured product family in the billing system).
    /// </summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the given eShopOnWeb user in a plan. Guaranteed idempotent: it ensures a
    /// single billing customer exists for the user (keyed by <see cref="SubscriptionEnrollment.UserReference"/>)
    /// and never creates a duplicate subscription for a plan the user is already subscribed to,
    /// so a double-click can never create two customers or two subscriptions.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions currently held by the given eShopOnWeb user. Returns an empty
    /// collection if the user has no billing customer record yet.
    /// </summary>
    Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);
}
