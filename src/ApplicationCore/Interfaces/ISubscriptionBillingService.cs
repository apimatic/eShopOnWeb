using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record.
/// Implementations translate provider failures into
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.BillingException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscription plans a shopper can subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given eShop user to the plan identified by <paramref name="planHandle"/>.
    /// Idempotent: ensures a single billing customer exists for the user and does not create a
    /// second subscription when a live one to the same plan already exists.
    /// </summary>
    /// <param name="userName">The eShop user's identity (their sign-in name / email).</param>
    /// <param name="planHandle">The stable handle of a plan from <see cref="GetPlansAsync"/>.</param>
    Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the given eShop user's subscriptions. Returns an empty list when the user has no
    /// billing customer record yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}
