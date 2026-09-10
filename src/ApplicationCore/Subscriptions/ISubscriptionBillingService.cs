using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Recurring-subscription billing, backed by the billing system of record (Maxio Advanced Billing).
/// The implementation owns all SDK interaction; callers work only with the domain types here.
/// Failures surface as <see cref="Exceptions.BillingException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscription plans available in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given user to a plan. Ensures a billing customer exists for the user (idempotent)
    /// and enrolls them (idempotent per user+plan), so a double-click never creates two customers or
    /// two subscriptions. When <paramref name="planHandle"/> is null the configured default plan is used.
    /// </summary>
    Task<SubscriptionResult> SubscribeAsync(BillingUser user, string? planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions belonging to the user identified by <paramref name="userReference"/>.</summary>
    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);
}
