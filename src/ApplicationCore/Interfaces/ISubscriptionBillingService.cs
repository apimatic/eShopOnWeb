using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by the external billing system of record.
/// All operations are idempotent: repeating a call never creates duplicate customers
/// or subscriptions.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscription plans available in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for the user and enrolls them in the given plan.
    /// Repeating the same (userId, productHandle) returns the existing subscription.
    /// </summary>
    Task<SubscriptionDto> SubscribeAsync(string userId, string email, string firstName, string lastName,
        string productHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the user's subscriptions; empty when the user has no billing customer yet.</summary>
    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}
