using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Subscription billing for eShopOnWeb users, with Maxio Advanced Billing as
/// the billing system of record. Implementations must be idempotent with
/// respect to user/plan enrollment: repeated calls for the same user and plan
/// must never create duplicate customers or subscriptions.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available for enrollment.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync();

    /// <summary>
    /// Enrolls the given user in the plan identified by <paramref name="planHandle"/>.
    /// Ensures a billing customer exists for the user (idempotently), then creates
    /// the subscription unless the user is already enrolled in that plan (in which
    /// case the existing subscription is returned unchanged).
    /// </summary>
    /// <exception cref="UnknownSubscriptionPlanException">
    /// Thrown when <paramref name="planHandle"/> is not an available plan.
    /// </exception>
    Task<SubscriptionSummary> SubscribeAsync(string username, string planHandle);

    /// <summary>
    /// Lists all subscriptions the given user holds in the billing system.
    /// Returns an empty list if the user has no billing customer yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionSummary>> GetMySubscriptionsAsync(string username);
}
