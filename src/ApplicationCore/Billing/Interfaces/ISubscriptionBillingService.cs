using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Billing.Interfaces;

/// <summary>
/// The application-facing port for recurring-subscription billing. The billing system of record
/// lives outside eShopOnWeb, so this interface deliberately exposes only the three operations the
/// subscribe flow needs and hides every provider detail behind the implementation.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans a shopper may subscribe to, i.e. the non-archived products of the
    /// configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the subscriber on a plan, creating the provider-side customer record first if it
    /// does not exist yet. The operation is idempotent: repeating it for a subscriber who is
    /// already enrolled on the plan returns the existing subscription with
    /// <see cref="SubscribeOutcome.AlreadySubscribed"/> instead of creating a second one.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription belonging to the subscriber, newest first. Returns an empty list
    /// when the subscriber has no provider-side customer record yet.
    /// </summary>
    Task<IReadOnlyList<SubscriberSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
