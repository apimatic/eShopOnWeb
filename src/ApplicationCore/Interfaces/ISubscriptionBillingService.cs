using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing against Maxio Advanced Billing (the system of record). Implementations
/// own idempotency and error translation; callers pass the eShop user's stable identity and get back
/// plain domain DTOs. All operations honor the supplied <see cref="CancellationToken"/> as the total
/// call budget.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscribable plans in the configured Maxio product family. Fully paginated (no silent truncation).</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Idempotently ensures a Maxio customer exists for <paramref name="userReference"/> and subscribes them to
    /// <paramref name="planHandle"/>. A repeated call for the same (user, plan) returns the existing subscription
    /// rather than creating a second one — safe under double-click and concurrent submits.
    /// </summary>
    /// <param name="userReference">The eShop user's stable identity (username), used as the Maxio customer reference.</param>
    /// <param name="email">The customer email to attach in Maxio (equal to the username in eShop).</param>
    /// <param name="planHandle">The product handle to subscribe to; must be one the plan list returns. When null/blank, the configured default (or the first available plan) is used.</param>
    Task<SubscribeOutcome> SubscribeAsync(string userReference, string email, string? planHandle, CancellationToken cancellationToken);

    /// <summary>Lists the caller's subscriptions from Maxio. Returns an empty list when the user has no Maxio customer yet.</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken);
}
