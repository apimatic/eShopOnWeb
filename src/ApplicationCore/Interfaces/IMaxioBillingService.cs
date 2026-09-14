using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing as the system of record.
/// Implementations talk to Maxio only; eShopOnWeb keeps no local subscription state.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>Lists the plans available to subscribe to (products in the configured Maxio product family).</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the user and enrolls them in the given plan.
    /// Idempotent: a repeated call (e.g. a double-click) never creates a second customer or a
    /// duplicate live subscription — the existing subscription is returned instead.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(MaxioCustomerIdentity identity, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subscriptions owned by the Maxio customer with the given reference.
    /// Returns an empty list when no such customer exists yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
