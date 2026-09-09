using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Orchestrates the eShopOnWeb user's recurring-subscription relationship
/// with Maxio Advanced Billing, which is the billing system of record.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the available subscription plans (products in the configured product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync();

    /// <summary>
    /// Ensures a Maxio customer exists for the user (idempotent, keyed by the user id as the
    /// Maxio customer reference) and creates the subscription unless the user already holds a
    /// live subscription to the same plan (so double-clicks never create duplicates).
    /// Returns the subscription and whether it already existed.
    /// </summary>
    Task<(SubscriptionDto Subscription, bool AlreadyExisted)> SubscribeAsync(
        string userId, string email, string planHandle);

    /// <summary>
    /// Lists the user's subscriptions as known to Maxio. Returns an empty list when the
    /// user does not have a Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userId);
}
