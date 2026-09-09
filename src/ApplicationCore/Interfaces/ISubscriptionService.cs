using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provides recurring-subscription billing backed by Maxio Advanced Billing.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available to shoppers.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync();

    /// <summary>
    /// Ensures a billing-system customer exists for the user (idempotently) and
    /// enrolls the user in the requested plan. If the user is already enrolled
    /// in the plan with a live subscription, that subscription is returned
    /// instead of creating a duplicate.
    /// </summary>
    /// <param name="userId">Stable local user id, used as the billing customer reference.</param>
    /// <param name="userName">User name used to derive billing customer details.</param>
    /// <param name="email">User email used for the billing customer.</param>
    /// <param name="planHandle">Handle of the plan to subscribe to; when null the configured default plan is used.</param>
    Task<SubscribeResult> SubscribeAsync(string userId, string userName, string email, string? planHandle = null);

    /// <summary>
    /// Lists the subscriptions the user holds in the billing system.
    /// </summary>
    Task<IReadOnlyList<UserSubscriptionInfo>> ListUserSubscriptionsAsync(string userId);
}
