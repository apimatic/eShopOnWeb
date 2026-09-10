using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application port for recurring-subscription billing. Implemented by an infrastructure adapter
/// (Maxio Advanced Billing). Keeps the billing provider out of the web/API layer.
/// </summary>
public interface ISubscriptionManagementService
{
    /// <summary>Lists the plans available to subscribe to, from the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given user to <paramref name="planHandle"/>. Ensures a single billing customer
    /// exists for the user and does not create a duplicate subscription when a live one already exists.
    /// </summary>
    Task<SubscriptionEnrollmentResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions currently held by the given user (empty if they have no billing customer yet).</summary>
    Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
