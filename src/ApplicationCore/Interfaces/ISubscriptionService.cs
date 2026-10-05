using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provides recurring-subscription billing backed by Maxio Advanced Billing
/// (the billing system of record). The existing one-time cart/checkout flow is unaffected.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans (Maxio products) available in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the user (idempotently, keyed on the user id as
    /// customer reference), then subscribes them to the requested plan. Subscribing twice to
    /// the same plan does not create a second subscription; the existing one is returned instead.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the user's subscriptions as recorded by Maxio. Returns an empty list when the
    /// user has no Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}