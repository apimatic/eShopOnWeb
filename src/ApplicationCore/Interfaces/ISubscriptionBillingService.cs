using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the recurring-subscription billing system of record
/// (Maxio Advanced Billing). Keeps the web/API layer free of any Maxio-specific
/// transport details.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans available to subscribe to (the products in the configured
    /// product family), excluding archived ones.
    /// </summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a user into a plan. Ensures a Maxio customer exists for the user
    /// (creating one if needed) and creates the subscription. The operation is
    /// idempotent: repeated calls for the same user+plan return the existing live
    /// subscription rather than creating duplicates.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the given user (matched by the Maxio
    /// customer reference). Returns an empty collection if the user has no Maxio
    /// customer record yet.
    /// </summary>
    Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);
}
