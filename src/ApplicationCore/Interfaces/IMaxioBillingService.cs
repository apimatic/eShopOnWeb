using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over Maxio Advanced Billing for the recurring-subscription capability.
/// The concrete implementation lives in Infrastructure and is the only place the Maxio
/// SDK is referenced; ApplicationCore and PublicApi stay SDK-agnostic.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>Lists the plans available for subscription within the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the user in a plan. Idempotent: ensures a single Maxio customer exists for the
    /// user (by reference) and does not create a duplicate subscription when the user already
    /// has an active subscription to the same plan.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions belonging to the Maxio customer identified by <paramref name="userReference"/>.</summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);
}
