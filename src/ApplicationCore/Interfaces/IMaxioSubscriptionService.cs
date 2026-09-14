using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application-facing subscription billing operations, backed by Maxio Advanced Billing.
/// </summary>
public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="username"/> and enrolls it in the
    /// given plan. Idempotent: a repeated call for a plan the account is already live on
    /// returns the existing subscription instead of creating a duplicate.
    /// </summary>
    Task<MaxioSubscription> SubscribeAsync(string username, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the account's subscriptions, or an empty list if it has no Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsAsync(string username, CancellationToken cancellationToken = default);
}
