using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing as the system of record.
/// Implementations translate provider/transport failures into
/// <see cref="Exceptions.SubscriptionBillingException"/> at this boundary.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscription plans available in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken ct = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the user (idempotent) and subscribes them to a plan.
    /// Safe to call twice for the same user/plan: an existing live subscription is returned rather than
    /// a duplicate being created.
    /// </summary>
    /// <param name="subscriber">The eShop user, resolved from the caller's token.</param>
    /// <param name="planHandle">The plan (product) handle to subscribe to; when null the default plan is used.</param>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string? planHandle, CancellationToken ct = default);

    /// <summary>Lists the subscriptions the user currently holds in Maxio (empty if they have no customer record).</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken ct = default);
}
