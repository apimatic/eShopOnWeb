using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by the external billing system of record.
/// Failures surface as <see cref="Exceptions.BillingProviderException"/>, <see cref="Exceptions.SubscriptionPlanNotFoundException"/>
/// or <see cref="Exceptions.SubscriptionInProgressException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the user has a billing customer and subscribes them to <paramref name="planHandle"/>.
    /// Idempotent per (user, plan): repeating the call returns the existing subscription instead of creating another.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}
