using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing as the system of record.
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the given user in a plan. Idempotent: if the user already has a live
    /// subscription to the plan, the existing subscription is returned unchanged.
    /// </summary>
    Task<SubscriptionDetails> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDetails>> ListUserSubscriptionsAsync(string userId, string email, CancellationToken cancellationToken = default);
}
