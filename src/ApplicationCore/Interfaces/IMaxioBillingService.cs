using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over Maxio Advanced Billing for the eShopOnWeb subscription capability.
/// Maxio is the system of record; there is no local persistence of subscriptions.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>Lists the subscription plans (products) available in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the caller in a plan. Ensures a Maxio customer exists for the user
    /// (idempotent) and creates the subscription, returning the existing live
    /// subscription instead of a duplicate when one is already present.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions belonging to the customer with the given reference.</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
