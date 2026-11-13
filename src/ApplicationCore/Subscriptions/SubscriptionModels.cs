using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <param name="IsComplete">
/// False when the plan list was cut short (page cap reached) — the list may be missing plans.
/// </param>
public sealed record SubscriptionPlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool IsComplete);

public sealed record SubscribeCommand(string UserName, string PlanHandle, string? FirstName = null, string? LastName = null);

public sealed record SubscriptionDetails(
    int Id,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    string State,
    long? PriceInCents,
    string? Currency,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CanceledAt);

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created by this request.</summary>
    Created,
    /// <summary>The user already had a live subscription to this plan; it is returned unchanged.</summary>
    AlreadySubscribed,
    /// <summary>
    /// The create call was sent but its outcome could not be confirmed (provider timeout/outage).
    /// It is recorded and settled by the next subscribe or my-subscriptions request.
    /// </summary>
    PendingConfirmation
}

public sealed record SubscribeResult(SubscribeOutcome Outcome, string PlanHandle, string SubscriptionReference, SubscriptionDetails? Subscription);

/// <summary>A subscribe request whose outcome is still being established.</summary>
public sealed record PendingSubscription(string PlanHandle, string SubscriptionReference, string Status, DateTimeOffset RequestedAt);

public sealed record CustomerSubscriptions(
    int? BillingCustomerId,
    IReadOnlyList<SubscriptionDetails> Subscriptions,
    IReadOnlyList<PendingSubscription> Pending);
