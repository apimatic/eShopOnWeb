using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Maxio.Contracts;

namespace Microsoft.eShopWeb.Maxio.Services;

/// <summary>
/// Orchestrates the Maxio subscription capability used by the public API:
/// catalog discovery, idempotent customer enrollment and subscription management.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the subscribable plans in the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Subscribes the given shopper to the given plan. Ensures a Maxio customer exists for the
    /// shopper (idempotent) and never creates a second live subscription for the same plan.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the subscriptions currently on file for the given shopper (Maxio customer reference).
    /// </summary>
    Task<IReadOnlyList<SubscriptionRecord>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken);
}
