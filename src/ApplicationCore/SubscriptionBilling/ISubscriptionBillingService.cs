using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans available in the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the shopper exists as a Maxio customer and subscribes them to the given plan.
    /// Idempotent: when the customer already holds a live subscription to the plan the existing
    /// subscription is returned instead of creating a duplicate.
    /// </summary>
    Task<SubscriptionSignupResult> SubscribeAsync(SubscriptionCustomer customer, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions owned by the shopper mapped to the given Maxio customer reference.
    /// Returns an empty list when no Maxio customer exists for the reference yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
