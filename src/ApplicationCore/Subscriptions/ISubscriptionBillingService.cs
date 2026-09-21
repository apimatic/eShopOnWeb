using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing as the system of record.
/// Implementations translate provider/transport failures into <see cref="Exceptions.SubscriptionBillingException"/>
/// and unknown plan handles into <see cref="Exceptions.PlanNotFoundException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans available to subscribe to (the configured product family's products).</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes <paramref name="subscriber"/> to the plan identified by <paramref name="planHandle"/>.
    /// Idempotent: ensures a Maxio customer exists for the user (never creating two), and returns the
    /// existing subscription when one is already present, so a double-click does not create duplicates.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions currently held by <paramref name="subscriber"/> (empty if the user has no Maxio customer yet).</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
