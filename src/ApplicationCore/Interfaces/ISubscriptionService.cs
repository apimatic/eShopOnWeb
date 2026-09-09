using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Identifies the eShopOnWeb buyer requesting subscription operations.
/// </summary>
public sealed record BuyerIdentity(string BuyerId, string Email, string FirstName, string? LastName);

/// <summary>
/// A subscription as reported back to API callers.
/// </summary>
public sealed record SubscriptionView(
    int BillingSubscriptionId,
    int BillingCustomerId,
    string PlanHandle,
    string PlanName,
    int PriceInCents,
    string State,
    DateTimeOffset? CurrentPeriodStartsAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CreatedAt);

/// <summary>
/// The result of a subscribe operation.
/// </summary>
public sealed record SubscribeResult(SubscriptionView Subscription, bool CreatedNew);

/// <summary>
/// Recurring subscription enrollment for eShopOnWeb buyers, backed by an external
/// billing system (Maxio Advanced Billing) as the system of record.
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently enrolls a buyer into the plan with the given handle:
    /// ensures a billing customer exists, then ensures a subscription to the plan exists.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(BuyerIdentity buyer, string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the buyer's subscriptions with live state from the billing system.
    /// </summary>
    Task<IReadOnlyList<SubscriptionView>> ListForBuyerAsync(BuyerIdentity buyer,
        CancellationToken cancellationToken = default);
}
