using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring subscription billing backed by Maxio Advanced Billing as the billing system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans available for purchase.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for the given eShopOnWeb user and enrolls them in the
    /// requested plan. Idempotent: repeated or concurrent calls for the same user and plan never
    /// create duplicate customers or subscriptions.
    /// </summary>
    Task<SubscriptionSummary> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the live subscriptions the given eShopOnWeb user holds with the billing system.
    /// Returns an empty list if the user has no billing customer yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionSummary>> ListUserSubscriptionsAsync(string userId, string email, CancellationToken cancellationToken = default);
}
