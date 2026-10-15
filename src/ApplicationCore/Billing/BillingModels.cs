using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>A plan a shopper can subscribe to, as offered by the billing provider.</summary>
public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <summary>
/// The plans offered for the configured product family. <see cref="Truncated"/> is true when the
/// provider had more plans than the read was allowed to walk, so <see cref="Plans"/> is incomplete.
/// </summary>
public sealed record SubscriptionPlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool Truncated);

/// <summary>A subscription as held by the billing provider (the system of record).</summary>
public sealed record BillingSubscription(
    int Id,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    string State,
    bool IsLive,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? CreatedAt,
    string? Reference);

/// <summary>The eShopOnWeb user a billing customer is created for.</summary>
public sealed record BillingSubscriber(string UserName, string Email);

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created by this request (or by an earlier attempt this request settled).</summary>
    Created,

    /// <summary>The user already has a live subscription to the plan; nothing new was created.</summary>
    AlreadySubscribed,

    /// <summary>The provider did not confirm the outcome; it is settled on the user's next request.</summary>
    Pending
}

public sealed record SubscribeResult(SubscribeOutcome Outcome, BillingSubscription? Subscription);
