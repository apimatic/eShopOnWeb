using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing against the billing system of record
/// (Maxio Advanced Billing). Implementations must be idempotent with respect
/// to the eShopOnWeb user: repeated calls must never create duplicate
/// billing customers or duplicate subscriptions on the same plan.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans available for subscription from the configured product family.
    /// </summary>
    Task<Result<IEnumerable<SubscriptionPlanInfo>>> GetPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a billing customer exists for the given eShopOnWeb user and enrolls
    /// them in the plan identified by <paramref name="planHandle"/>. If the user
    /// already has a live subscription on that plan, the existing subscription is
    /// returned with <see cref="SubscriptionEnrollment.AlreadySubscribed"/> set.
    /// </summary>
    /// <param name="userName">The eShopOnWeb username (email) from the authenticated principal.</param>
    /// <param name="planHandle">The stable handle of the plan to subscribe to.</param>
    /// <param name="idempotencyKey">
    /// Optional caller-supplied key forwarded to the billing API's uniqueness token
    /// so that a retried create is rejected as a duplicate rather than applied twice.
    /// </param>
    Task<Result<SubscriptionEnrollment>> SubscribeAsync(
        string userName, string planHandle, string? idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Lists all of the user's subscriptions in the billing system of record.
    /// </summary>
    Task<Result<IEnumerable<SubscriptionSummary>>> GetSubscriptionsForUserAsync(
        string userName, CancellationToken cancellationToken);
}
