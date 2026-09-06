using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing. The billing provider - not this application - is the system of
/// record, so every operation reads through to it rather than to a local store.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the plans a shopper may subscribe to, cheapest first.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrols a shopper in a plan. The operation is idempotent: a Maxio customer is created for the
    /// shopper only once, and repeating the same request returns the subscription that already
    /// exists instead of creating a second one.
    /// </summary>
    /// <exception cref="Exceptions.SubscriptionPlanNotFoundException">The plan handle is not offered.</exception>
    /// <exception cref="Exceptions.BillingProviderException">The billing provider rejected or could not serve the request.</exception>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription held by the shopper, most recently created first. Returns an empty
    /// list when the shopper has never subscribed.
    /// </summary>
    Task<IReadOnlyList<Subscription>> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}
