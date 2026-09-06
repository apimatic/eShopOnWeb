using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Enrollment and read access to a shopper's recurring subscriptions.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Enrolls a shopper onto a plan, creating the billing customer first if one does not exist yet.
    /// </summary>
    /// <remarks>
    /// The operation is idempotent: repeating it for the same subscriber and plan returns the
    /// existing subscription with <see cref="SubscribeResult.Created"/> set to <c>false</c> instead
    /// of enrolling the shopper twice.
    /// </remarks>
    /// <exception cref="Exceptions.SubscriptionPlanNotFoundException">
    /// The requested plan handle does not exist in the configured product family.
    /// </exception>
    /// <exception cref="Exceptions.BillingProviderException">
    /// The billing provider rejected or could not serve the request.
    /// </exception>
    Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription the shopper holds, newest first. Returns an empty list when the
    /// shopper has no billing customer yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        SubscriberProfile subscriber,
        CancellationToken cancellationToken = default);
}
