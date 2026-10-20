using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>The signed-in shopper being enrolled; <see cref="UserName"/> identifies them to the billing provider.</summary>
public record Subscriber(string UserName, string Email);

/// <summary>A recurring plan offered by the billing provider.</summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <summary>
/// The plans offered. <see cref="Truncated"/> is true when the provider had more plans than were read,
/// so the list is not complete.
/// </summary>
public record SubscriptionPlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool Truncated);

/// <summary>A subscription as the billing provider reports it.</summary>
public record SubscriptionSummary(
    int? SubscriptionId,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? CreatedAt);

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created by this call.</summary>
    Created,

    /// <summary>The user already held a subscription to this plan; nothing new was created.</summary>
    AlreadySubscribed,

    /// <summary>
    /// The provider may have created the subscription but did not confirm it. The outcome is settled
    /// on the next request; the caller must not assume failure.
    /// </summary>
    Pending
}

public record SubscribeResult(SubscribeOutcome Outcome, string PlanHandle, SubscriptionSummary? Subscription);
