using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record (Maxio).
/// This abstraction keeps the ApplicationCore free of any Maxio SDK dependency; the
/// implementation lives in the Infrastructure layer.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscription plans available for the configured product family.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for the shopper (idempotent by external reference) and
    /// enrolls them in the requested plan. Enrolling a shopper who already has an active
    /// subscription to the same plan returns the existing subscription rather than creating a
    /// duplicate, so a double-click never creates two customers or two subscriptions.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions currently held by the shopper with the given external reference.</summary>
    Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);
}
