using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port over Maxio Advanced Billing for recurring-subscription billing. The implementation talks to
/// Maxio strictly through the endpoints and schemas defined in the Maxio OpenAPI specification.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available to shoppers — the products in the configured Maxio
    /// product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the given eShopOnWeb user in a plan, ensuring a Maxio customer exists first. The whole
    /// operation is idempotent on the subscriber's reference: repeating it (e.g. a double-click) never
    /// creates a second customer, and never creates a second live subscription to the same plan — the
    /// existing one is returned instead.
    /// </summary>
    /// <param name="subscriber">The authenticated user, projected into Maxio customer fields.</param>
    /// <param name="planHandle">
    /// The handle of the plan to subscribe to. When null or empty, the default plan of the configured
    /// product family is used.
    /// </param>
    Task<CustomerSubscription> SubscribeAsync(
        SubscriberIdentity subscriber,
        string? planHandle = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions held by the given eShopOnWeb user. Returns an empty list when the user
    /// has no Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
