using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Fronts the recurring-billing provider (Maxio Advanced Billing) for the Subscribe capability.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing-provider customer exists for the given reference (idempotent), then enrolls
    /// that customer in the given plan. Returns the existing active/trialing subscription on that plan
    /// instead of creating a duplicate, where one is already found.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(
        string customerReference,
        string customerEmail,
        string customerFirstName,
        string customerLastName,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions for the customer identified by <paramref name="customerReference"/>.
    /// Returns an empty list when no billing-provider customer exists yet for that reference.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken = default);
}
