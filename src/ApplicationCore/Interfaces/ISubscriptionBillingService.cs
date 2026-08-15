using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port onto the recurring-subscription billing system of record. The implementation lives in
/// Infrastructure and is the only place that talks to the billing provider SDK; callers depend
/// only on this abstraction and the provider-agnostic models in
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Subscriptions"/>.
/// Every method throws <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.BillingException"/>
/// on provider failures.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscription plans available in the configured product family.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a shopper in a plan. Idempotent: ensures a single billing customer exists for the
    /// user (keyed on <see cref="SubscribeRequest.UserReference"/>) and will not create a second
    /// live subscription to the same plan — a repeat call returns the existing enrollment.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the given user. Returns an empty collection when the
    /// user has no billing customer yet.
    /// </summary>
    Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default);
}
