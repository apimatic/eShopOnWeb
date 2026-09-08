using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing as the system of record.
/// Implementations translate all provider failures into
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.MaxioBillingException"/> so callers
/// see a single failure type carrying a caller-safe message and status.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>Lists the plans available in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="subscriber"/> (idempotent) and enrolls
    /// them in the plan identified by <paramref name="productHandle"/>. Subscribing again for the
    /// same (user, plan) returns the existing subscription rather than creating a duplicate.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions belonging to <paramref name="subscriber"/> (empty if they have no Maxio customer yet).</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
