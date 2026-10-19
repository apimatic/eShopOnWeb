using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>The plans offered by this storefront.</summary>
    Task<SubscriptionPlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for <paramref name="subscriber"/> and enrolls them in the plan.
    /// Repeating the call for a plan the subscriber already holds returns the existing subscription.
    /// </summary>
    Task<SubscribeOutcome> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>The subscriber's subscriptions to plans in this storefront's catalog.</summary>
    Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);

    /// <summary>Settles claims whose provider outcome is unknown or whose owner abandoned them.</summary>
    Task<int> SettleUnresolvedClaimsAsync(CancellationToken cancellationToken = default);
}

/// <summary>The eShop user being billed.</summary>
public record Subscriber(string UserName, string Email);

public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit,
    bool RequiresPaymentMethod);

/// <param name="IsComplete">False when the catalog was cut short by the page cap; the list is then partial.</param>
public record SubscriptionPlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool IsComplete);

public record BillingSubscription(
    int Id,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? CreatedAt);

public enum SubscribeStatus
{
    /// <summary>A new subscription was created by this call.</summary>
    Created,

    /// <summary>The subscriber already holds a live subscription to the plan; nothing was created.</summary>
    AlreadySubscribed,

    /// <summary>The create was sent but its outcome could not be confirmed yet; it is being reconciled.</summary>
    Pending
}

public record SubscribeOutcome(SubscribeStatus Status, BillingSubscription? Subscription, string? SubscriptionReference);
