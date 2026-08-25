using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing operations, backed by Maxio Advanced Billing
/// as the billing system of record. The eShopOnWeb user id is stored as the
/// Maxio customer <c>reference</c>, which makes customer/subscription creation idempotent.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscription plans available in the configured Maxio product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the user and enrolls them in the given plan.
    /// Idempotent: repeated calls for the same user + plan return the existing subscription
    /// instead of creating duplicates.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(string userId, string email, string? displayName, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the user's subscriptions. Returns an empty list if the user has no Maxio customer yet.</summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}
