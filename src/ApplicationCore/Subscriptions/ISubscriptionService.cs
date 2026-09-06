using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the plans a shopper may subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for the user and enrolls them on the requested plan.
    /// Safe to retry: repeating the call for the same user and plan returns the existing
    /// subscription rather than creating a second one.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions held by the given user, newest first. Returns an empty list when
    /// the user has no billing customer yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string userIdentifier,
        CancellationToken cancellationToken = default);
}
