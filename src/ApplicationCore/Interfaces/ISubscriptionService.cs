using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing with Maxio Advanced Billing as the billing
/// system of record. All operations are idempotent with respect to the
/// eShopOnWeb user: repeated calls never create duplicate customers or
/// subscriptions.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the plans available for subscription (products of the configured
    /// Maxio product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes a user to a plan. Ensures a Maxio customer exists for the
    /// user first. If the user is already subscribed to the plan, the existing
    /// subscription is returned with <see cref="SubscribeResult.Created"/> set
    /// to false.
    /// </summary>
    /// <param name="userKey">Stable unique key of the user taken from the authenticated identity.</param>
    /// <param name="email">The user's email address.</param>
    /// <param name="productHandle">The API handle of the plan (Maxio product) to subscribe to.</param>
    Task<SubscribeResult> SubscribeAsync(string userKey, string email, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the user's subscriptions as recorded in Maxio Advanced Billing.
    /// </summary>
    Task<IReadOnlyList<UserSubscription>> ListUserSubscriptionsAsync(string userKey, string email, CancellationToken cancellationToken = default);
}
