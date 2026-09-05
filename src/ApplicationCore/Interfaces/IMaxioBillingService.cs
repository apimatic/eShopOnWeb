using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Fronts Maxio Advanced Billing, the system of record for subscription billing state.
/// Implementations must make customer/subscription enrollment idempotent so that a retried
/// call never creates duplicate Maxio entities for the same app user.
/// </summary>
public interface IMaxioBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="userId"/> and enrolls it in the plan
    /// identified by <paramref name="planHandle"/>. Safe to call more than once for the same
    /// user/plan: an existing live subscription to that plan is returned as-is rather than
    /// creating a second one.
    /// </summary>
    Task<UserSubscription> SubscribeAsync(string userId, string userEmail, string planHandle, CancellationToken ct);

    Task<IReadOnlyList<UserSubscription>> ListMySubscriptionsAsync(string userId, CancellationToken ct);
}
