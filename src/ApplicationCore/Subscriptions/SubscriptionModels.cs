using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>A plan a shopper can subscribe to.</summary>
public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <summary>
/// The plans offered by the configured product family.
/// <see cref="Truncated"/> is true when the catalog was larger than the read cap, so <see cref="Plans"/> is incomplete.
/// </summary>
public sealed record SubscriptionPlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool Truncated);

/// <summary>A subscription as the billing provider currently reports it.</summary>
public sealed record SubscriptionDetails(
    int SubscriptionId,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt);

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created by this request.</summary>
    Created,

    /// <summary>The user already holds this plan; the existing subscription is returned (idempotent replay).</summary>
    AlreadySubscribed,

    /// <summary>The provider may have created the subscription but it could not be confirmed yet; it is settled on the next request.</summary>
    PendingConfirmation
}

/// <summary>The result of a subscribe request. <see cref="Subscription"/> is null only for <see cref="SubscribeOutcome.PendingConfirmation"/>.</summary>
public sealed record SubscribeResult(SubscribeOutcome Outcome, string PlanHandle, SubscriptionDetails? Subscription);
