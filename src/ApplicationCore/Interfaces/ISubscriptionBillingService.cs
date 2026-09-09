using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing as the
/// billing system of record. Implementations must be idempotent with respect
/// to the eShopOnWeb user identity: repeated calls for the same user and plan
/// must never produce duplicate customers or duplicate live subscriptions.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans available for subscription from the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for the given eShopOnWeb user, then enrolls
    /// them in the plan identified by <paramref name="planHandle"/>. If the user already
    /// holds a live subscription to that plan, it is returned without creating a duplicate.
    /// </summary>
    /// <exception cref="Exceptions.PlanNotFoundException">The plan handle is unknown or not subscribable.</exception>
    /// <exception cref="Exceptions.MaxioApiException">The billing system rejected or failed the request.</exception>
    Task<SubscribeResult> SubscribeAsync(string userId, string userName, string email, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the caller's subscriptions as recorded in the billing system of record.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> ListUserSubscriptionsAsync(string userId, string userName, string email, CancellationToken cancellationToken = default);
}
