using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing capability, backed by Maxio Advanced Billing as the system
/// of record. This is an additive, parallel capability to the one-time Catalog/Basket/Order flow.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans available for shoppers to subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the user in a plan. Ensures a Maxio customer exists for the user (idempotent by
    /// reference) and creates the subscription. If the user is already enrolled in the plan, the
    /// existing subscription is returned instead of creating a duplicate.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the user identified by <paramref name="userReference"/>.
    /// Returns an empty list if the user has no Maxio customer record yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);
}
