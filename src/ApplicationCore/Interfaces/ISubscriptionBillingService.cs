using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing capability, backed by the billing system of record.
/// All operations are idempotent with respect to the eShopOnWeb user.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans available for subscription in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the user to a plan, provisioning a billing customer for the user
    /// when one does not yet exist. If the user already has a live subscription on
    /// the plan, that subscription is returned (outcome AlreadySubscribed) instead
    /// of creating a duplicate.
    /// </summary>
    Task<SubscriptionSignupResult> SubscribeAsync(
        BillingCustomerInfo customer,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the user's subscriptions from the billing system.
    /// </summary>
    Task<IReadOnlyList<UserSubscription>> GetUserSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken = default);
}