using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates subscription billing against Maxio Advanced Billing, which is
/// the system of record for plans, customers and subscriptions. eShopOnWeb
/// keeps no local copy of this state.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the subscribable plans in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="customer"/> and enrolls them in
    /// the plan identified by <paramref name="planHandle"/>. If the customer already has a
    /// live subscription to that plan, that existing subscription is returned instead of
    /// creating a duplicate - this is what makes a double-click safe.
    /// </summary>
    Task<MaxioSubscription> SubscribeAsync(MaxioCustomerProfile customer, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the Maxio customer referenced by <paramref name="customerReference"/>.
    /// Returns an empty list if no Maxio customer has been provisioned for this reference yet.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
