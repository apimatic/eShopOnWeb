using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing as the system of record.
/// Implementations translate all provider/transport failures into <see cref="SubscriptionBillingException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscribable plans in the configured product family.</summary>
    Task<SubscriptionPlanList> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the caller (idempotent) and enrolls them in <paramref name="planHandle"/>.
    /// A repeated call for the same buyer+plan returns the existing subscription without a second provider write.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the caller's subscriptions as reflected in Maxio (empty if they have no customer yet).</summary>
    Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
