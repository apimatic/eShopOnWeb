using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by an external billing system of record.
/// Implementations must be idempotent: repeating a subscribe request for the same shopper and plan
/// resolves to the same subscription instead of creating another one.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Plans a shopper may subscribe to, as published by the billing system.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Looks up a plan by its stable handle, or null when no such plan is published.</summary>
    Task<SubscriptionPlan?> GetPlanAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the shopper exists as a customer in the billing system and enrolls them on
    /// <paramref name="planHandle"/>. Safe to call repeatedly.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>All subscriptions belonging to the shopper; empty when they have no billing customer yet.</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
